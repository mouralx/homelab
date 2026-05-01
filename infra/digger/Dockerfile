FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /digger

COPY ./ /src

RUN dotnet publish /src/Digger/Digger.csproj -o /digger

RUN rm -rf /src

ENTRYPOINT ["dotnet", "/digger/Digger.dll"]
