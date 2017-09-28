$vsixPath = "$($env:USERPROFILE)\nanoFramework.Tools.VS2017.Extension.vsix"
(New-Object Net.WebClient).DownloadFile('http://bit.ly/nugetizer-2017', $vsixPath)
"`"C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\Common7\IDE\VSIXInstaller.exe`" /q /a $vsixPath" | out-file ".\install-vsix.cmd" -Encoding ASCII
& .\install-vsix.cmd