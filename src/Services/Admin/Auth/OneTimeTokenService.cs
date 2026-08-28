using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;

namespace ToBeClarify.Api.Services.Admin.Auth;

public sealed class OneTimeTokenService : IOneTimeTokenService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly OneTimeTokenOptions _options;
    private readonly string _filePath;
    private readonly string _lockFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OneTimeTokenService(
        IOptions<OneTimeTokenOptions> options,
        IWebHostEnvironment environment)
    {
        _options = options.Value;
        _filePath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(_options.FilePath) ? "oneTimeToken.txt" : _options.FilePath,
            environment.ContentRootPath);
        _lockFilePath = $"{_filePath}.lock";
    }

    public Task<OneTimeTokenIssueResult> IssueAsync(
        string purpose,
        string? targetUserId,
        string issuedBy,
        CancellationToken cancellationToken)
        => WithLockAsync(async () =>
        {
            var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(_options.ExpirationMinutes, 1, 60));
            var record = new OneTimeTokenFile
            {
                KeyHash = HashKey(key),
                Purpose = purpose,
                TargetUserId = targetUserId,
                IssuedBy = issuedBy,
                ExpiresAt = expiresAt
            };

            await WriteAtomicallyAsync(record, cancellationToken);
            return new OneTimeTokenIssueResult(key, expiresAt);
        }, cancellationToken);

    public Task<T> ConsumeAsync<T>(
        string submittedKey,
        string expectedPurpose,
        string? expectedTargetUserId,
        Func<OneTimeTokenContext, Task<T>> action,
        CancellationToken cancellationToken)
        => WithLockAsync(async () =>
        {
            var record = await ReadAsync(cancellationToken);
            if (!IsValidRecord(record) ||
                !string.Equals(record!.Purpose, expectedPurpose, StringComparison.Ordinal) ||
                !string.Equals(record.TargetUserId, expectedTargetUserId, StringComparison.Ordinal))
            {
                throw InvalidToken(expectedPurpose);
            }

            var normalizedSubmittedKey = submittedKey.Trim();
            if (string.IsNullOrWhiteSpace(normalizedSubmittedKey) ||
                !FixedTimeEquals(record.KeyHash, HashKey(normalizedSubmittedKey)))
            {
                throw InvalidToken(expectedPurpose);
            }

            if (record.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                await DeleteAsync();
                throw ExpiredToken(expectedPurpose);
            }

            var context = new OneTimeTokenContext(record.Purpose, record.TargetUserId, record.IssuedBy);
            var result = await action(context);
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

        throw new BusinessException("驗證碼功能目前忙碌中，請稍後再試。", "ONE_TIME_TOKEN_BUSY");
    }

    private async Task<OneTimeTokenFile?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) return null;

        try
        {
            await using var stream = new FileStream(
                _filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<OneTimeTokenFile>(
                stream, SerializerOptions, cancellationToken);
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

    private async Task WriteAtomicallyAsync(OneTimeTokenFile record, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, record, SerializerOptions, cancellationToken);
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

    private static bool IsValidRecord(OneTimeTokenFile? record)
        => record is not null &&
           !string.IsNullOrWhiteSpace(record.KeyHash) &&
           !string.IsNullOrWhiteSpace(record.Purpose) &&
           !string.IsNullOrWhiteSpace(record.IssuedBy);

    private static string HashKey(string key)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    private static bool FixedTimeEquals(string left, string right)
    {
        try
        {
            var leftBytes = Convert.FromHexString(left);
            var rightBytes = Convert.FromHexString(right);
            return leftBytes.Length == rightBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static BusinessException InvalidToken(string purpose)
        => purpose == OneTimeTokenPurpose.StaffRegister
            ? new BusinessException("註冊驗證碼不存在、錯誤或用途不符。", "REGISTER_KEY_INVALID")
            : new BusinessException("密碼重設驗證碼不存在、錯誤或與指定帳號不符。", "PASSWORD_RESET_KEY_INVALID");

    private static BusinessException ExpiredToken(string purpose)
        => purpose == OneTimeTokenPurpose.StaffRegister
            ? new BusinessException("註冊驗證碼已過期。", "REGISTER_KEY_EXPIRED")
            : new BusinessException("密碼重設驗證碼已過期。", "PASSWORD_RESET_KEY_EXPIRED");

    private sealed class OneTimeTokenFile
    {
        public string KeyHash { get; init; } = string.Empty;
        public string Purpose { get; init; } = string.Empty;
        public string? TargetUserId { get; init; }
        public string IssuedBy { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; init; }
    }
}
