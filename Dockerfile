# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy toàn bộ mã nguồn vào thư mục làm việc hiện tại
COPY . .

# Khôi phục và build publish trực tiếp
RUN dotnet restore Appwebbongda.csproj
RUN dotnet publish Appwebbongda.csproj -c Release -o /app/publish

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Appwebbongda.dll"]