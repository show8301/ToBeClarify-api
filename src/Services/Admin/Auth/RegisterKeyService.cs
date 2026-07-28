using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Admin.Auth;

public sealed class RegisterKeyService : IRegisterKeyService
{
    private readonly RegisterKeyOptions _options;
    private readonly string _filePath;
    private readonly string _lockFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RegisterKeyService(
        IOptions<RegisterKeyOptions> options,
        IWebHostEnvironment environment)
    {
        _options = options.Value;
        _filePath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(_options.FilePath) ? "registerKey.txt" : _options.FilePath,
            environment.ContentRootPath);
        _lockFilePath = $"{_filePath}.lock";
    }

    public Task<AdminRegisterKeyDto> IssueAsync(CancellationToken cancellationToken)
        => WithLockAsync(async () =>
        {
            var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(_options.ExpirationMinutes, 1, 60));
            var record = new RegisterKeyFile(key, expiresAt);

            await WriteAtomicallyAsync(record, cancellationToken);
            return new AdminRegisterKeyDto(key, expiresAt);
        }, cancellationToken);

    public Task<T> ConsumeAsync<T>(
        string submittedKey,
        Func<Task<T>> registration,
        CancellationToken cancellationToken)
        => WithLockAsync(async () =>
        {
            var record = await ReadAsync(cancellationToken)
                ?? throw new BusinessException("註冊驗證碼不存在或已失效。", "REGISTER_KEY_INVALID");

            var normalizedSubmittedKey = submittedKey.Trim();
            if (string.IsNullOrWhiteSpace(normalizedSubmittedKey) ||
                !FixedTimeEquals(record.Key, normalizedSubmittedKey))
                throw new BusinessException("註冊驗證碼錯誤。", "REGISTER_KEY_INVALID");

            if (record.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                await DeleteAsync();
                throw new BusinessException("註冊驗證碼已過期。", "REGISTER_KEY_EXPIRED");
            }

            var result = await registration();
            await DeleteAsync();
            return result;
        }, cancellationToken);

    private async Task<T> WithLockAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var fileLock = await AcquireFileLockAsync(cancellationToken);
            return await action();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FileStream> AcquireFileLockAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_lockFilePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        for (var attempt = 0; attempt < 200; attempt++)
        {
            try
            {
                return new FileStream(
                    _lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.Asynchronous);
            }
            catch (IOException) when (attempt < 199)
            {
                await Task.Delay(25, cancellationToken);
            }
        }

        throw new BusinessException("註冊功能目前忙碌中，請稍後再試。", "REGISTER_KEY_BUSY");
    }

    private async Task<RegisterKeyFile?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) return null;

        try
        {
            await using var stream = new FileStream(
                _filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<RegisterKeyFile>(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private async Task WriteAtomicallyAsync(RegisterKeyFile record, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, record, cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private Task DeleteAsync()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
        return Task.CompletedTask;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private sealed record RegisterKeyFile(string Key, DateTimeOffset ExpiresAt);
}
