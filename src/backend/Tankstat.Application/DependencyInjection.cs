using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tankstat.Application.Access;
using Tankstat.Application.Auth;
using Tankstat.Application.Expenses;
using Tankstat.Application.Health;
using Tankstat.Application.Images;
using Tankstat.Application.Photos;
using Tankstat.Application.Imports;
using Tankstat.Application.Notifications;
using Tankstat.Application.Odometers;
using Tankstat.Application.Recognition;
using Tankstat.Application.Recurring;
using Tankstat.Application.Refuelings;
using Tankstat.Application.Settings;
using Tankstat.Application.Sync;
using Tankstat.Application.Sharing;
using Tankstat.Application.Stats;
using Tankstat.Application.Users;
using Tankstat.Application.Vehicles;

namespace Tankstat.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VehicleDefaultsOptions>().Bind(configuration.GetSection(VehicleDefaultsOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<VehicleDefaultsOptions>, VehicleDefaultsOptionsValidator>());
        services.AddOptions<NotificationOptions>().Bind(configuration.GetSection(NotificationOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NotificationOptions>, NotificationOptionsValidator>());
        services.AddOptions<SyncOptions>().Bind(configuration.GetSection(SyncOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SyncOptions>, SyncOptionsValidator>());
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName));
        services.AddOptions<SmtpOptions>().Bind(configuration.GetSection(SmtpOptions.SectionName));
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName)).ValidateOnStart();
        // Photo reading is optional: its settings are never validated on start (they would stop the app); RecognitionSetup turns
        // reading off instead when they cannot be used.
        services.AddOptions<RecognitionOptions>().Bind(configuration.GetSection(RecognitionOptions.SectionName));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>());

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<HealthReporter>();
        services.AddSingleton<NoticeService>();

        services.AddScoped<AccessService>();
        services.AddScoped<AccessAdminService>();
        services.AddScoped<SessionService>();
        services.AddScoped<PasswordResetService>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserSessionService>();
        services.AddScoped<UserService>();
        services.AddScoped<VehicleService>();
        services.AddScoped<ImageService>();
        services.AddScoped<LogAccessGuard>();
        services.AddScoped<LogPhotoAccess>();
        services.AddScoped<LogPhotoService>();
        services.AddScoped<PhotoDraftService>();
        services.AddScoped<RefuelingService>();
        services.AddScoped<ExpenseService>();
        services.AddScoped<RecurringExpenseService>();
        services.AddScoped<StatsService>();
        services.AddScoped<ChartService>();
        services.AddScoped<UiSettingsService>();
        services.AddScoped<VehicleOrderService>();
        services.AddScoped<OfflineSettingsService>();
        services.AddScoped<OfflineFeedService>();
        services.AddSingleton<ImportSessionStore>();
        services.AddSingleton<IImportParser, FuelioCsvParser>();
        services.AddScoped<ImportService>();
        services.AddScoped<OdometerService>();
        services.AddScoped<ResourceSharingService>();
        services.AddScoped<Notifier>();
        services.AddScoped<RecurringNotificationSync>();
        services.AddScoped<NotificationService>();
        services.AddSingleton<RecognitionSetup>();
        services.AddSingleton<RecognitionAvailability>();
        services.AddScoped<RecognitionService>();
        services.AddScoped<LogPhotoFiller>();
        services.AddScoped<PhotoReadingProcessor>();
        return services;
    }
}
