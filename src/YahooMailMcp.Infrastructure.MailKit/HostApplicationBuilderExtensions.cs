using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using YahooMailMcp.Application;

namespace YahooMailMcp.Infrastructure.MailKit;

public static class HostApplicationBuilderExtensions
{
    extension(IHostApplicationBuilder builder)
    {
        public IHostApplicationBuilder AddYahooMailInfrastructure()
        {
            builder.Services.AddOptions<YahooOptions>()
                .BindConfiguration(YahooOptions.SectionName)
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<YahooOptions>, YahooOptionsValidator>();

            builder.Services.AddOptions<MailRuntimeOptions>()
                .BindConfiguration(MailRuntimeOptions.SectionName);

            builder.Services.AddOptions<CursorOptions>()
                .BindConfiguration(CursorOptions.SectionName)
                .ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<CursorOptions>, CursorOptionsValidator>();

            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<MailRuntimeOptions>>().Value;
                return new MailLimits(
                    options.DefaultPageSize,
                    options.MaxPageSize,
                    options.MaxBodyCharacters,
                    options.HardMaxBodyCharacters).Validate();
            });
            builder.Services.AddSingleton<ICursorCodec>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<CursorOptions>>().Value;
                return new CursorCodec(
                    options.SigningKey!,
                    TimeSpan.FromMinutes(options.MaximumAgeMinutes),
                    sp.GetRequiredService<TimeProvider>());
            });
            builder.Services.AddSingleton(sp => new DestinationFolderPolicy(
                sp.GetRequiredService<IOptions<YahooOptions>>().Value.TrashFolderDenyList));
            builder.Services.AddSingleton<IImapAuthenticator, AppPasswordImapAuthenticator>();
            builder.Services.AddSingleton<YahooImapSession>();
            builder.Services.AddSingleton<IYahooMailGateway, YahooMailGateway>();

            return builder;
        }
    }
}