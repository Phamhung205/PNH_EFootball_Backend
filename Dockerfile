# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Chi dinh thang file .csproj de tranh Docker tu tim file .slnx (tro sai duong dan cu)
COPY *.csproj ./
RUN dotnet restore Appwebbongda.csproj

COPY . ./
RUN dotnet publish Appwebbongda.csproj -c Release -o /app/publish

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# Render cap cong qua bien PORT; ASP.NET Core doc qua ASPNETCORE_HTTP_PORTS
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Appwebbongda.dll"]