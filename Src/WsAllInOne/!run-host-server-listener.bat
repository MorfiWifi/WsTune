@echo off
start "Host" cmd /k dotnet run host --urls http://localhost:10992
start "Listener" cmd /k dotnet run listener
start "Server" cmd /k dotnet run server