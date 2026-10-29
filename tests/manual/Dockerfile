FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY tests/manual/.publish/ .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ClothingAPI.dll"]
