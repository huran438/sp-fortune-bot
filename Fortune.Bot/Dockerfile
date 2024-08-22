# Use the official .NET runtime as a base image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

# Use the official .NET SDK as a build environment
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Fortune.Bot.csproj", "./"]
RUN dotnet restore "./Fortune.Bot.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "Fortune.Bot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Fortune.Bot.csproj" -c Release -o /app/publish

# Final stage, build the runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Fortune.Bot.dll"]
