FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /digger

COPY ./ /src

RUN dotnet publish /src/Digger.Worker/Digger.Worker.csproj -o /digger

RUN rm -rf /src

ENTRYPOINT ["dotnet", "/digger/Digger.Worker.dll"]
    