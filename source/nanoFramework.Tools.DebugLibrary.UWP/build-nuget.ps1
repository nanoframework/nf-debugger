nuget restore ..\nanoFramework.Tools.Debugger.sln

msbuild  /tv:15.0 /p:VisualStudioVersion=15.0 /t:pack nanoFramework.Tools.DebugLibrary.UWP.csproj /p:Configuration=Release
