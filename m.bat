dotnet new sln -n FinancialStreamer

dotnet new classlib -n FinancialStreamer.Core
dotnet sln add FinancialStreamer.Core

dotnet new classlib -n FinancialStreamer.Infrastructure
dotnet sln add FinancialStreamer.Infrastructure

dotnet new webapi -n FinancialStreamer.Api
dotnet sln add FinancialStreamer.Api

dotnet new web -n FinancialStreamer.WebSocket
dotnet sln add FinancialStreamer.WebSocket

cd FinancialStreamer.Core
dotnet add package Microsoft.Extensions.Logging

cd ../FinancialStreamer.Infrastructure
dotnet add package Newtonsoft.Json
dotnet add package WebSocketSharp-netstandard
dotnet add reference ../FinancialStreamer.Core/FinancialStreamer.Core.csproj

cd ../FinancialStreamer.Api
dotnet add reference ../FinancialStreamer.Core/FinancialStreamer.Core.csproj
dotnet add reference ../FinancialStreamer.Infrastructure/FinancialStreamer.Infrastructure.csproj

cd ../FinancialStreamer.WebSocket
dotnet add reference ../FinancialStreamer.Core/FinancialStreamer.Core.csproj
dotnet add reference ../FinancialStreamer.Infrastructure/FinancialStreamer.Infrastructure.csproj