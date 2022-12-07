"Updating dependents of nano-debugger" | Write-Host

# compute authorization header in format "AUTHORIZATION: basic 'encoded token'"
# 'encoded token' is the Base64 of the string "nfbot:personal-token"
$auth = "basic $([System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("nfbot:$env:GH_TOKEN"))))"

# because it can take sometime for the package to become available on the NuGet providers
# need to hang here for 1 minutes (1 * 60)
"Waiting 1 minute to let package process flow in Azure Artifacts feed..." | Write-Host
Start-Sleep -Seconds 60 

# init/reset these
$prTitle = ""
$newBranchName = "develop-nfbot/update-dependencies/" + [guid]::NewGuid().ToString()
$packageTargetVersion = gh release view --json tagName --jq .tagName

# working directory is agent temp directory
Write-Debug "Changing working directory to $env:Agent_TempDirectory"
Set-Location "$env:Agent_TempDirectory" | Out-Null

# clone repo and checkout develop branch
Write-Debug "Init and featch nf-Visual-Studio-extension repo"

####################
# VS 2019 & 2022

"********************************************************************************" | Write-Host
"Updating nanoFramework.Tools.Debugger.Net package in VS2019 & VS2022 solution..." | Write-Host

git clone --depth 1 https://github.com/nanoframework/nf-Visual-Studio-extension repo
Set-Location repo | Out-Null
git config --global gc.auto 0
git config --global user.name nfbot
git config --global user.email nanoframework@outlook.com
git config --global core.autocrlf true

Write-Host "Checkout develop branch..."
git checkout --quiet develop | Out-Null

dotnet remove VisualStudio.Extension-2019/VisualStudio.Extension-vs2019.csproj package nanoFramework.Tools.Debugger.Net
dotnet add VisualStudio.Extension-2019/VisualStudio.Extension-vs2019.csproj package nanoFramework.Tools.Debugger.Net
dotnet remove VisualStudio.Extension-2022/VisualStudio.Extension-vs2022.csproj package nanoFramework.Tools.Debugger.Net
dotnet add VisualStudio.Extension-2022/VisualStudio.Extension-vs2022.csproj package nanoFramework.Tools.Debugger.Net

"Bumping nanoFramework.Tools.Debugger to $packageTargetVersion." | Write-Host -ForegroundColor Cyan                

# build commit message
$commitMessage += "Bumps nanoFramework.Tools.Debugger to $packageTargetVersion.`n"
# build PR title
$prTitle = "Bumps nanoFramework.Tools.Debugger to $packageTargetVersion"

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
    # we are hardcoding to 'develop' branch to have a fixed one
    # this is very important for tags (which don't have branch information)
    # considering that the base branch can be changed at the PR ther is no big deal about this 
    $prRequestBody = @{title="$prTitle";body="$commitMessage";head="$newBranchName";base="develop"} | ConvertTo-Json
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
    Write-Host "Nothing udpate at VS extension."
}

#######################
# nanoFrameworkDeployer

"**************************************************************************************" | Write-Host
"Updating nanoFramework.Tools.Debugger.Net package in nanoFrameworkDeployer solution..." | Write-Host

Set-Location "$env:Agent_TempDirectory" | Out-Null

# clone repo and checkout main branch
Write-Debug "Init and featch nf-Deployer repo"

git clone --depth 1 https://github.com/nanoframework/nanoFrameworkDeployer nanoFrameworkDeployer
Set-Location nanoFrameworkDeployer | Out-Null
git config --global gc.auto 0
git config --global user.name nfbot
git config --global user.email nanoframework@outlook.com
git config --global core.autocrlf true

Write-Host "Checkout main branch..."
git checkout --quiet main | Out-Null

dotnet remove nanoFrameworkDeployer/nanoFrameworkDeployer.csproj package nanoFramework.Tools.Debugger.Net
dotnet add nanoFrameworkDeployer/nanoFrameworkDeployer.csproj package nanoFramework.Tools.Debugger.Net

"Bumping nanoFramework.Tools.Debugger to $packageTargetVersion." | Write-Host -ForegroundColor Cyan                

# build commit message
$commitMessage = "Bumps nanoFramework.Tools.Debugger to $packageTargetVersion.`n"
# build PR title
$prTitle = "Bumps nanoFramework.Tools.Debugger to $packageTargetVersion"

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
    $githubApiEndpoint = "https://api.github.com/repos/nanoframework/nanoFrameworkDeployer/pulls"
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
    Write-Host "Nothing udpate at nanoFramework Deployer."
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

dotnet remove nanoFirmwareFlasher.Library/nanoFirmwareFlasher.Library.csproj package nanoFramework.Tools.Debugger.Net
dotnet add nanoFirmwareFlasher.Library/nanoFirmwareFlasher.Library.csproj package nanoFramework.Tools.Debugger.Net
dotnet remove nanoFirmwareFlasher.Tool/nanoFirmwareFlasher.Tool.csproj package nanoFramework.Tools.Debugger.Net
dotnet add nanoFirmwareFlasher.Tool/nanoFirmwareFlasher.Tool.csproj package nanoFramework.Tools.Debugger.Net
nuget restore nanoFirmwareFlasher.sln

"Bumping nanoFramework.Tools.Debugger to $packageTargetVersion." | Write-Host -ForegroundColor Cyan                

# build commit message
$commitMessage = "Bumps nanoFramework.Tools.Debugger to $packageTargetVersion.`n"
# build PR title
$prTitle = "Bumps nanoFramework.Tools.Debugger to $packageTargetVersion"

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
    Write-Host "Nothing udpate at nano firmware flasher."
}
