using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LTS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UseWorkingDays = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoadingPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoadingPoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MilestoneAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    MilestoneType = table.Column<int>(type: "int", nullable: false),
                    OldValue = table.Column<DateOnly>(type: "date", nullable: true),
                    NewValue = table.Column<DateOnly>(type: "date", nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PartnerId = table.Column<int>(type: "int", nullable: true),
                    IntegrationRunId = table.Column<int>(type: "int", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MilestoneAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Partners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Partners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    AdapterKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SecretName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PollIntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ManualOverrideWins = table.Column<bool>(type: "bit", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSuccessAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Cursor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationSources_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LookupValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    CountryId = table.Column<int>(type: "int", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LookupValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LookupValues_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stores_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    UserType = table.Column<int>(type: "int", nullable: false),
                    PartnerId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleClaims_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IntegrationSourceId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MessagesReceived = table.Column<int>(type: "int", nullable: false),
                    MessagesProcessed = table.Column<int>(type: "int", nullable: false),
                    MessagesFailed = table.Column<int>(type: "int", nullable: false),
                    ShipmentsCreated = table.Column<int>(type: "int", nullable: false),
                    ShipmentsUpdated = table.Column<int>(type: "int", nullable: false),
                    TransfersCreated = table.Column<int>(type: "int", nullable: false),
                    TransfersUpdated = table.Column<int>(type: "int", nullable: false),
                    MilestonesApplied = table.Column<int>(type: "int", nullable: false),
                    UnmappedCodeCount = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationRuns_IntegrationSources_IntegrationSourceId",
                        column: x => x.IntegrationSourceId,
                        principalTable: "IntegrationSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StatusMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IntegrationSourceId = table.Column<int>(type: "int", nullable: false),
                    RawCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RawDescription = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    MilestoneType = table.Column<int>(type: "int", nullable: true),
                    IsIgnored = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatusMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatusMappings_IntegrationSources_IntegrationSourceId",
                        column: x => x.IntegrationSourceId,
                        principalTable: "IntegrationSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KpiTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Step = table.Column<int>(type: "int", nullable: false),
                    ExportTypeId = table.Column<int>(type: "int", nullable: true),
                    LoadingCountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    ArrivalCountryId = table.Column<int>(type: "int", nullable: true),
                    TargetDays = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KpiTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KpiTargets_Countries_ArrivalCountryId",
                        column: x => x.ArrivalCountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KpiTargets_LookupValues_ExportTypeId",
                        column: x => x.ExportTypeId,
                        principalTable: "LookupValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReferenceNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    InvoiceNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ArrivalCountryId = table.Column<int>(type: "int", nullable: false),
                    ArrivalCustomsId = table.Column<int>(type: "int", nullable: true),
                    ExportTypeId = table.Column<int>(type: "int", nullable: true),
                    TransportTypeId = table.Column<int>(type: "int", nullable: true),
                    LoadingPointId = table.Column<int>(type: "int", nullable: true),
                    LogisticsCompanyId = table.Column<int>(type: "int", nullable: true),
                    BrokerId = table.Column<int>(type: "int", nullable: true),
                    LoadingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DepartureCustomsClearanceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DepartureDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ArrivalToTargetCountryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CustomsStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CustomsEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CrossdockArrivalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TransferCount = table.Column<int>(type: "int", nullable: false),
                    TotalBoxes = table.Column<int>(type: "int", nullable: false),
                    TotalItems = table.Column<int>(type: "int", nullable: false),
                    CurrentStatus = table.Column<int>(type: "int", nullable: false),
                    CurrentStatusDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Performance = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shipments_Countries_ArrivalCountryId",
                        column: x => x.ArrivalCountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_LoadingPoints_LoadingPointId",
                        column: x => x.LoadingPointId,
                        principalTable: "LoadingPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_LookupValues_ArrivalCustomsId",
                        column: x => x.ArrivalCustomsId,
                        principalTable: "LookupValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_LookupValues_ExportTypeId",
                        column: x => x.ExportTypeId,
                        principalTable: "LookupValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_LookupValues_TransportTypeId",
                        column: x => x.TransportTypeId,
                        principalTable: "LookupValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_Partners_BrokerId",
                        column: x => x.BrokerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_Partners_LogisticsCompanyId",
                        column: x => x.LogisticsCompanyId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserClaims_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserCountryAccess",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountryId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCountryAccess", x => new { x.UserId, x.CountryId });
                    table.ForeignKey(
                        name: "FK_UserCountryAccess_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserCountryAccess_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_UserLogins_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPagePermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountryId = table.Column<int>(type: "int", nullable: true),
                    PageKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CanView = table.Column<bool>(type: "bit", nullable: false),
                    CanEdit = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPagePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPagePermissions_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserPagePermissions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_UserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IntegrationRunId = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EntityReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RawStatusCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationMessages_IntegrationRuns_IntegrationRunId",
                        column: x => x.IntegrationRunId,
                        principalTable: "IntegrationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    TransferNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TotalBoxes = table.Column<int>(type: "int", nullable: false),
                    TotalItems = table.Column<int>(type: "int", nullable: false),
                    CrossdockDepartureDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PlannedStoreArrivalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    StoreArrivalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    StorePreAcceptanceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    StoreAcceptanceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CurrentStatus = table.Column<int>(type: "int", nullable: false),
                    CurrentStatusDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Performance = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transfers_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Transfers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Countries_Code",
                table: "Countries",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationMessages_EntityReference",
                table: "IntegrationMessages",
                column: "EntityReference");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationMessages_IntegrationRunId_Status",
                table: "IntegrationMessages",
                columns: new[] { "IntegrationRunId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationRuns_IntegrationSourceId_StartedAt",
                table: "IntegrationRuns",
                columns: new[] { "IntegrationSourceId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationSources_CountryId_Kind",
                table: "IntegrationSources",
                columns: new[] { "CountryId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_KpiTargets_ArrivalCountryId",
                table: "KpiTargets",
                column: "ArrivalCountryId");

            migrationBuilder.CreateIndex(
                name: "IX_KpiTargets_ExportTypeId",
                table: "KpiTargets",
                column: "ExportTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_KpiTargets_Step",
                table: "KpiTargets",
                column: "Step");

            migrationBuilder.CreateIndex(
                name: "IX_KpiTargets_Step_ExportTypeId_LoadingCountryCode_ArrivalCountryId_EffectiveFrom",
                table: "KpiTargets",
                columns: new[] { "Step", "ExportTypeId", "LoadingCountryCode", "ArrivalCountryId", "EffectiveFrom" },
                unique: true,
                filter: "[ExportTypeId] IS NOT NULL AND [LoadingCountryCode] IS NOT NULL AND [ArrivalCountryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LoadingPoints_Code",
                table: "LoadingPoints",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoadingPoints_CountryCode",
                table: "LoadingPoints",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_LookupValues_CountryId",
                table: "LookupValues",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_LookupValues_Kind_CountryId_Code",
                table: "LookupValues",
                columns: new[] { "Kind", "CountryId", "Code" },
                unique: true,
                filter: "[CountryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MilestoneAudits_ChangedAt",
                table: "MilestoneAudits",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MilestoneAudits_Scope_EntityId",
                table: "MilestoneAudits",
                columns: new[] { "Scope", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_MilestoneAudits_ShipmentId_ChangedAt",
                table: "MilestoneAudits",
                columns: new[] { "ShipmentId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Partners_Type_Code",
                table: "Partners",
                columns: new[] { "Type", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleClaims_RoleId",
                table: "RoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "Roles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ArrivalCountryId_BrokerId",
                table: "Shipments",
                columns: new[] { "ArrivalCountryId", "BrokerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ArrivalCountryId_CurrentStatus",
                table: "Shipments",
                columns: new[] { "ArrivalCountryId", "CurrentStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ArrivalCountryId_LogisticsCompanyId",
                table: "Shipments",
                columns: new[] { "ArrivalCountryId", "LogisticsCompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ArrivalCountryId_Performance",
                table: "Shipments",
                columns: new[] { "ArrivalCountryId", "Performance" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ArrivalCustomsId",
                table: "Shipments",
                column: "ArrivalCustomsId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_BrokerId",
                table: "Shipments",
                column: "BrokerId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ExportTypeId",
                table: "Shipments",
                column: "ExportTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_InvoiceNo",
                table: "Shipments",
                column: "InvoiceNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_LoadingPointId",
                table: "Shipments",
                column: "LoadingPointId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_LogisticsCompanyId",
                table: "Shipments",
                column: "LogisticsCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ReferenceNo",
                table: "Shipments",
                column: "ReferenceNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TransportTypeId",
                table: "Shipments",
                column: "TransportTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_StatusMappings_IntegrationSourceId_RawCode",
                table: "StatusMappings",
                columns: new[] { "IntegrationSourceId", "RawCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stores_CountryId_Code",
                table: "Stores",
                columns: new[] { "CountryId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_ShipmentId",
                table: "Transfers",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_StoreId_StoreArrivalDate",
                table: "Transfers",
                columns: new[] { "StoreId", "StoreArrivalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_TransferNo",
                table: "Transfers",
                column: "TransferNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserClaims_UserId",
                table: "UserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCountryAccess_CountryId",
                table: "UserCountryAccess",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLogins_UserId",
                table: "UserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPagePermissions_CountryId",
                table: "UserPagePermissions",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPagePermissions_UserId_CountryId_PageKey",
                table: "UserPagePermissions",
                columns: new[] { "UserId", "CountryId", "PageKey" },
                unique: true,
                filter: "[CountryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "Users",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Users_PartnerId",
                table: "Users",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "Users",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntegrationMessages");

            migrationBuilder.DropTable(
                name: "KpiTargets");

            migrationBuilder.DropTable(
                name: "MilestoneAudits");

            migrationBuilder.DropTable(
                name: "RoleClaims");

            migrationBuilder.DropTable(
                name: "StatusMappings");

            migrationBuilder.DropTable(
                name: "Transfers");

            migrationBuilder.DropTable(
                name: "UserClaims");

            migrationBuilder.DropTable(
                name: "UserCountryAccess");

            migrationBuilder.DropTable(
                name: "UserLogins");

            migrationBuilder.DropTable(
                name: "UserPagePermissions");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "UserTokens");

            migrationBuilder.DropTable(
                name: "IntegrationRuns");

            migrationBuilder.DropTable(
                name: "Shipments");

            migrationBuilder.DropTable(
                name: "Stores");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "IntegrationSources");

            migrationBuilder.DropTable(
                name: "LoadingPoints");

            migrationBuilder.DropTable(
                name: "LookupValues");

            migrationBuilder.DropTable(
                name: "Partners");

            migrationBuilder.DropTable(
                name: "Countries");
        }
    }
}
