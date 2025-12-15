FROM mcr.microsoft.com/dotnet/sdk:8.0
WORKDIR /app

COPY . .

RUN dotnet restore sw_crm_contact_search.csproj && dotnet restore tests/tests.csproj
RUN dotnet build sw_crm_contact_search.csproj -c Release
RUN dotnet build repository_before/repository_before.csproj -c Release
RUN dotnet build repository_after/repository_after.csproj -c Release

CMD ["dotnet", "test", "tests/tests.csproj", "-c", "Release", "-v", "minimal"]
