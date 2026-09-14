# Giai đoạn 1: dùng .NET SDK để restore, build và publish source code.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY ["ClothingAPI.csproj", "."]

RUN dotnet restore "ClothingAPI.csproj"

COPY . .

RUN dotnet publish "ClothingAPI.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false


# Giai đoạn 2: image chạy thật, nhỏ hơn image build.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "ClothingAPI.dll"]