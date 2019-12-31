mkdir Temp
cd Temp
nuget install Microsoft.CodeCoverage -Version 16.4.0
cd ..

New-Item -Type Directory -Force -Path "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\Team Tools"
Move-Item -Path "Temp\Microsoft.CodeCoverage.16.4.0\build\netstandard1.0\CodeCoverage" -Destination "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\Team Tools\Dynamic Code Coverage Tools"
Move-Item -Path "Temp\Microsoft.CodeCoverage.16.4.0\lib" -Destination "C:\Program Files (x86)\Microsoft Visual Studio\2019\lib"
Remove-Item -Recurse -Force Temp
