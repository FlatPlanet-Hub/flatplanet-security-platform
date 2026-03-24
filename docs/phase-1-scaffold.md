# Phase 1 — Project Scaffold

## Goal
Stand up the .NET 10 solution with correct architecture, DB connection, middleware, and health check.

## Tasks

- [x] Create .NET 10 solution (`FlatPlanet.Security`)
- [x] Create 4 projects: API, Application, Domain, Infrastructure
- [x] Set up project references (API → Application → Domain, Infrastructure → Domain)
- [x] Add NuGet packages
- [x] `IDbConnectionFactory` + Npgsql implementation
- [x] `appsettings.json` structure + options classes (SupabaseOptions, JwtOptions)
- [x] Global exception handling middleware
- [x] Security headers middleware
- [x] Health check endpoint (`GET /health`)
- [x] Wire up `Program.cs`

## Packages

| Package | Project |
|---|---|
| Dapper | Infrastructure |
| Npgsql | Infrastructure |
| Microsoft.AspNetCore.Authentication.JwtBearer | API |
| System.IdentityModel.Tokens.Jwt | Application |
| Microsoft.Extensions.Configuration.Abstractions | Application |

## Status: COMPLETE
