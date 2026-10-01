# Copyright (c) .NET Foundation and Contributors
# See LICENSE file in the project root for full license information.

"Updating dependents of nano-debugger" | Write-Host

# compute authorization header in format "AUTHORIZATION: basic 'encoded token'"
# 'encoded token' is the Base64 of the string "nfbot:personal-token"
$auth = "basic $([System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("nfbot:$env:GITHUB_TOKEN")))"

# init/reset these
$prTitle = ""
$newBranchName = "develop-nfbot/update-dependencies/" + [guid]::NewGuid().ToString()
$packageName = "nanoframework.tools.debugger.net"
$repoBranch = "main"

# resolve target version: prefer explicit TARGET_VERSION env var, fall back to the build tag
if (![string]::IsNullOrWhiteSpace($env:TARGET_VERSION)) {
    $packageTargetVersion = $env:TARGET_VERSION
    $explicitVersion = $true
    Write-Host "Using TARGET_VERSION from environment: $packageTargetVersion"
}
else {
    $packageTargetVersion = $env:Build_SourceBranch
    $explicitVersion = $false

    # check if this is running from a checked out tag
    if ($packageTargetVersion -notlike "refs/tags/*") {
        throw "ERROR: Branch name is not a tag and TARGET_VERSION is not set! Either set TARGET_VERSION or checkout a tag before calling."
    }
}

# normalize version: accepts '1.2.3', 'v1.2.3', 'V1.2.3', 'refs/tags/v1.2.3', surrounding quotes/spaces and build metadata
$packageTargetVersion = $packageTargetVersion -replace "^[\s'`"]+|[\s'`"]+$", ""
$packageTargetVersion = $packageTargetVersion -replace "^refs/tags/", ""
$packageTargetVersion = $packageTargetVersion -replace "^[vV]", ""
$packageTargetVersion = $packageTargetVersion -replace "\+.*$", ""

if ($packageTargetVersion -notmatch "^\d+\.\d+\.\d+(-[0-9A-Za-z\.-]+)?$") {
    throw "ERROR: '$packageTargetVersion' is not a valid package version."
}

$isPreview = $packageTargetVersion -match "preview"

if ($isPreview) {
    # switch to develop branch for preview versions
    $repoBranch = "develop"
}

# working directory is agent temp directory
Write-Debug "Changing working directory to $env:Agent_TempDirectory"
Set-Location "$env:Agent_TempDirectory" | Out-Null

# clone repo and checkout
Write-Debug "Init and featch nf-Visual-Studio-extension repo"

####################
# VS 2019 & 2022

"********************************************************************************" | Write-Host
"Updating nanoFramework.Tools.Debugger.Net package in VS2019 & VS2022 solution..." | Write-Host

git clone --depth 1 --branch $repoBranch https://github.com/nanoframework/nf-Visual-Studio-extension repo

if ($LASTEXITCODE -ne 0) {
    throw "ERROR: Failed to clone branch '$repoBranch' from nf-Visual-Studio-extension."
}

Set-Location repo | Out-Null
git config --global gc.auto 0
git config --global user.name nfbot
git config --global user.email nanoframework@outlook.com
git config --global core.autocrlf true

Write-Host "Checked out $repoBranch branch."

# check if nuget package is already available from nuget.org
$nugetApiUrl = "https://api.nuget.org/v3-flatcontainer/$packageName/index.json"

function Test-NugetVersionAvailable {
    param (
        [string]$url,
        [string]$targetVersion,
        [bool]$exactMatch,
        [bool]$preview
    )
    try {
        $versions = @((Invoke-RestMethod -Uri $url -Method Get).versions)
    }
    catch {
        Write-Warning "Error querying NuGet API: $_"
        return $false
    }

    if ($exactMatch) {
        # explicit version: just check that it has been published (-contains is case-insensitive)
        return $versions -contains $targetVersion
    }

    # no explicit version: check that the target is the latest one published for its track (preview or stable)
    if ($preview) {
        $versions = @($versions | Where-Object { $_ -match "preview" })
    }
    else {
        $versions = @($versions | Where-Object { $_ -notmatch "preview" })
    }

    if ($versions.Count -eq 0) {
        return $false
    }

    Write-Host "Latest version on nuget.org feed: $($versions[-1])"

    return $versions[-1] -eq $targetVersion
}

$script:cancelCheckWarned = $false

function Test-BuildCanceled {
    if ([string]::IsNullOrEmpty($env:SYSTEM_ACCESSTOKEN) -or
        [string]::IsNullOrEmpty($env:SYSTEM_COLLECTIONURI) -or
        [string]::IsNullOrEmpty($env:SYSTEM_TEAMPROJECTID) -or
        [string]::IsNullOrEmpty($env:BUILD_BUILDID)) {

        if (-not $script:cancelCheckWarned) {
            Write-Warning "SYSTEM_ACCESSTOKEN not available, can't check if the build was canceled."
            $script:cancelCheckWarned = $true
        }

        return $false
    }

    try {
        $buildUrl = "$($env:SYSTEM_COLLECTIONURI.TrimEnd('/'))/$($env:SYSTEM_TEAMPROJECTID)/_apis/build/builds/$($env:BUILD_BUILDID)?api-version=7.1"
        $build = Invoke-RestMethod -Uri $buildUrl -Method Get -Headers @{ Authorization = "Bearer $env:SYSTEM_ACCESSTOKEN" }

        return ($build.status -eq "cancelling") -or ($build.result -eq "canceled")
    }
    catch {
        if (-not $script:cancelCheckWarned) {
            Write-Warning "Error checking build status: $_"
            $script:cancelCheckWarned = $true
        }

        return $false
    }
}

if ($explicitVersion) {
    Write-Host "Target version is: $packageTargetVersion (explicit, waiting for this version)."
}
else {
    $track = if ($isPreview) { "preview" } else { "stable" }
    Write-Host "Target version is: $packageTargetVersion (from tag, waiting for it to be the latest $track version)."
}

while (-not (Test-NugetVersionAvailable -url $nugetApiUrl -targetVersion $packageTargetVersion -exactMatch $explicitVersion -preview $isPreview)) {
    Write-Host "Target version ($packageTargetVersion) still not available from nuget.org feed. Waiting 5 minutes..."
    Start-Sleep -Seconds 300

    # exit if the build was canceled meanwhile
    if (Test-BuildCanceled) {
        Write-Host "Build was canceled. Exiting wait loop."
        exit 0
    }
}

Write-Host "Version $packageTargetVersion available from nuget.org feed. Proceeding with update."

dotnet restore
dotnet remove VisualStudio.Extension-2019/VisualStudio.Extension-vs2019.csproj package nanoFramework.Tools.Debugger.Net 
dotnet add VisualStudio.Extension-2019/VisualStudio.Extension-vs2019.csproj package nanoFramework.Tools.Debugger.Net --version $packageTargetVersion --no-restore 
dotnet remove VisualStudio.Extension-2022/VisualStudio.Extension-vs2022.csproj package nanoFramework.Tools.Debugger.Net
dotnet add VisualStudio.Extension-2022/VisualStudio.Extension-vs2022.csproj package nanoFramework.Tools.Debugger.Net --version $packageTargetVersion --no-restore 
nuget restore -uselockfile

"Bumping nanoFramework.Tools.Debugger to v$packageTargetVersion." | Write-Host -ForegroundColor Cyan                

# build commit message
$commitMessage += "Bumps nanoFramework.Tools.Debugger to v$packageTargetVersion.`n"
# build PR title
$prTitle = "Bumps nanoFramework.Tools.Debugger to v$packageTargetVersion"

# need this line so nfbot flags the PR appropriately
$commitMessage += "`n[version update]`n`n"

# better add this warning line               
$commitMessage += "### :warning: This is an automated update. Merge only after all tests pass. :warning:`n"

Write-Debug "Git branch" 

# check if anything was changed
$repoStatus = "$(git status --short --porcelain)"

if ($repoStatus -ne "")
{
    # create branch to perform updates
    git branch $newBranchName

    Write-Debug "Checkout branch" 

    # checkout branch
    git checkout $newBranchName

    Write-Debug "Add changes" 

    # commit changes
    git add -A > $null

    Write-Debug "Commit changed files"

    git commit -m "$prTitle ***NO_CI***" -m "$commitMessage" > $null

    Write-Debug "Push changes"

    git -c http.extraheader="AUTHORIZATION: $auth" push --set-upstream origin $newBranchName > $null

    # start PR
    # we are pointing to the selected repo branch
    # considering that the base branch can be changed at the PR there is no big deal about this
    $prRequestBody = @{title="$prTitle";body="$commitMessage";head="$newBranchName";base="$repoBranch"} | ConvertTo-Json
    $githubApiEndpoint = "https://api.github.com/repos/nanoframework/nf-Visual-Studio-extension/pulls"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    $headers = @{}
    $headers.Add("Authorization","$auth")
    $headers.Add("Accept","application/vnd.github.symmetra-preview+json")

    try 
    {
        $result = Invoke-RestMethod -Method Post -UserAgent [Microsoft.PowerShell.Commands.PSUserAgent]::InternetExplorer -Uri  $githubApiEndpoint -Header $headers -ContentType "application/json" -Body $prRequestBody
        'Started PR with dependencies update...' | Write-Host -NoNewline
        'OK' | Write-Host -ForegroundColor Green

        # add labels to PR
        $prNumber = $result.number

        gh pr edit $prNumber --add-label "VS2019"
        gh pr edit $prNumber --add-label "VS2022"
    }
    catch 
    {
        $result = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($result)
        $reader.BaseStream.Position = 0
        $reader.DiscardBufferedData()
        $responseBody = $reader.ReadToEnd();

        throw "Error starting PR: $responseBody"
    }
}
else
{
    Write-Host "Nothing to udpate at VS extension."
}

#######################
# nano firmware flasher

"**************************************************************************************" | Write-Host
"Updating nanoFramework.Tools.Debugger.Net package in nano firmware flasher solution..." | Write-Host

Set-Location "$env:Agent_TempDirectory" | Out-Null

# clone repo and checkout main branch
Write-Debug "Init and featch nf-Deployer repo"

git clone --depth 1 https://github.com/nanoframework/nanoFirmwareFlasher nanoFirmwareFlasher
Set-Location nanoFirmwareFlasher | Out-Null
git config --global gc.auto 0
git config --global user.name nfbot
git config --global user.email nanoframework@outlook.com
git config --global core.autocrlf true

Write-Host "Checkout main branch..."
git checkout --quiet main | Out-Null

dotnet restore
dotnet remove nanoFirmwareFlasher.Library/nanoFirmwareFlasher.Library.csproj package nanoFramework.Tools.Debugger.Net
dotnet add nanoFirmwareFlasher.Library/nanoFirmwareFlasher.Library.csproj package nanoFramework.Tools.Debugger.Net --version $packageTargetVersion --no-restore 
dotnet remove nanoFirmwareFlasher.Tool/nanoFirmwareFlasher.Tool.csproj package nanoFramework.Tools.Debugger.Net
dotnet add nanoFirmwareFlasher.Tool/nanoFirmwareFlasher.Tool.csproj package nanoFramework.Tools.Debugger.Net --version $packageTargetVersion --no-restore 
dotnet remove nanoFirmwareFlasher.Tests/nanoFirmwareFlasher.Tests.csproj package nanoFramework.Tools.Debugger.Net
dotnet add nanoFirmwareFlasher.Tests/nanoFirmwareFlasher.Tests.csproj package nanoFramework.Tools.Debugger.Net --version $packageTargetVersion --no-restore 
dotnet restore --force-evaluate

"Bumping nanoFramework.Tools.Debugger to v$packageTargetVersion." | Write-Host -ForegroundColor Cyan                

# build commit message
$commitMessage = "Bumps nanoFramework.Tools.Debugger to v$packageTargetVersion.`n"
# build PR title
$prTitle = "Bumps nanoFramework.Tools.Debugger to v$packageTargetVersion"

# need this line so nfbot flags the PR appropriately
$commitMessage += "`n[version update]`n`n"

# add this to cascade updates
$commitMessage += "`n`n***UPDATE_DEPENDENTS***`n`n"

# better add this warning line               
$commitMessage += "### :warning: This is an automated update. Merge only after all tests pass. :warning:`n"

Write-Debug "Git branch" 

# create branch to perform updates
git branch $newBranchName

Write-Debug "Checkout branch" 

# checkout branch
git checkout $newBranchName

# check if anything was changed
$repoStatus = "$(git status --short --porcelain)"

if ($repoStatus -ne "")
{
    Write-Debug "Add changes" 

    # commit changes
    git add -A > $null

    Write-Debug "Commit changed files"

    git commit -m "$prTitle ***NO_CI***" -m "$commitMessage" > $null

    Write-Debug "Push changes"

    git -c http.extraheader="AUTHORIZATION: $auth" push --set-upstream origin $newBranchName > $null

    # start PR
    # we are hardcoding to 'main' branch to have a fixed one
    # this is very important for tags (which don't have branch information)
    # considering that the base branch can be changed at the PR ther is no big deal about this 
    $prRequestBody = @{title="$prTitle";body="$commitMessage";head="$newBranchName";base="main"} | ConvertTo-Json
    $githubApiEndpoint = "https://api.github.com/repos/nanoframework/nanoFirmwareFlasher/pulls"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    $headers = @{}
    $headers.Add("Authorization","$auth")
    $headers.Add("Accept","application/vnd.github.symmetra-preview+json")

    try 
    {
        $result = Invoke-RestMethod -Method Post -UserAgent [Microsoft.PowerShell.Commands.PSUserAgent]::InternetExplorer -Uri  $githubApiEndpoint -Header $headers -ContentType "application/json" -Body $prRequestBody
        'Started PR with dependencies update...' | Write-Host -NoNewline
        'OK' | Write-Host -ForegroundColor Green
    }
    catch 
    {
        $result = $_.Exception.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($result)
        $reader.BaseStream.Position = 0
        $reader.DiscardBufferedData()
        $responseBody = $reader.ReadToEnd();

        throw "Error starting PR: $responseBody"
    }
}
else
{
    Write-Host "Nothing to udpate at nano firmware flasher."
}
