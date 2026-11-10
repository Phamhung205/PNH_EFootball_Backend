# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy toàn bộ mã nguồn để Docker nhận diện chính xác cấu trúc thư mục và file csproj
COPY . ./

# Khôi phục và publish trực tiếp từ file project của dự án
RUN dotnet restore "Appwebbongda.csproj"
RUN dotnet publish "Appwebbongda.csproj" -c Release -o /app/publish

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Render cấp cổng qua biến PORT cho ASP.NET Core
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Appwebbongda.dll"]