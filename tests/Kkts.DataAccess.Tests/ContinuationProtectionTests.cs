using System.Text;
using Kkts.DataAccess.DataProtection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class ContinuationProtectionTests
{
    [Fact]
    public void Encryption_restart_rotation_isolation_and_key_loss_preserve_the_contract()
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "kda-cursor-" + Guid.NewGuid().ToString("N"))
        );
        var lost = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "kda-cursor-" + Guid.NewGuid().ToString("N"))
        );
        try
        {
            var payload = Encoding.UTF8.GetBytes("hidden-boundary-and-context");
            var provider = DataProtectionProvider.Create(
                directory,
                builder => builder.SetApplicationName("test")
            );
            var protector = new DataProtectionContinuationTokenProtector(provider);
            var encrypted = protector.Protect(payload);
            Assert.DoesNotContain("hidden-boundary", Encoding.UTF8.GetString(encrypted));
            var restarted = new DataProtectionContinuationTokenProtector(
                DataProtectionProvider.Create(
                    directory,
                    builder => builder.SetApplicationName("test")
                )
            );
            Assert.Equal(payload, restarted.Unprotect(encrypted));
            var services = new ServiceCollection();
            services
                .AddDataProtection()
                .PersistKeysToFileSystem(directory)
                .SetApplicationName("test");
            using (var container = services.BuildServiceProvider())
                container
                    .GetRequiredService<IKeyManager>()
                    .CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));
            var rotated = new DataProtectionContinuationTokenProtector(
                DataProtectionProvider.Create(
                    directory,
                    builder => builder.SetApplicationName("test")
                )
            );
            Assert.Equal(payload, rotated.Unprotect(encrypted));
            var tampered = encrypted.ToArray();
            tampered[tampered.Length / 2] ^= 1;
            Assert.Throws<InvalidContinuationTokenException>(() => rotated.Unprotect(tampered));
            Assert.Throws<InvalidContinuationTokenException>(() =>
                rotated.Unprotect(provider.CreateProtector("wrong-purpose").Protect(payload))
            );
            var isolated = new DataProtectionContinuationTokenProtector(
                DataProtectionProvider.Create(
                    directory,
                    builder => builder.SetApplicationName("different")
                )
            );
            Assert.Throws<InvalidContinuationTokenException>(() => isolated.Unprotect(encrypted));
            var emptyKeys = new DataProtectionContinuationTokenProtector(
                DataProtectionProvider.Create(lost, builder => builder.SetApplicationName("test"))
            );
            Assert.Throws<InvalidContinuationTokenException>(() => emptyKeys.Unprotect(encrypted));
        }
        finally
        {
            foreach (var owned in new[] { directory, lost })
            {
                foreach (var file in owned.GetFiles())
                    file.Delete();
                owned.Delete();
            }
        }
    }
}
