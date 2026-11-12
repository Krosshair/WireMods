copy /y "%~dp0src\*.cs" "%~dp0..\..\src\"
copy /y "%~dp0CHANGELOG.md" "%~dp0..\..\package\"
copy /y "%~dp0README.md" "%~dp0..\..\package\"
rem files removed from src by the strip are in src here too: restoring copies them back
