using System.Text.Json;
using NtftEnhanced.Hardening;
using NtftEnhanced.Protection;

namespace NtftEnhanced.Ipc;

public sealed class ControlApi
{
    private readonly ProtectionCoordinator _coordinator;
    private readonly Infrastructure.ILog _log;

    public ControlApi(ProtectionCoordinator coordinator, Infrastructure.ILog log)
    {
        _coordinator = coordinator;
        _log = log;
    }

    public string Handle(string line)
    {
        Request? request;
        try
        {
            request = JsonSerializer.Deserialize<Request>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return Write(false, "İstek okunamadı.");
        }
        if (request?.Op is null)
            return Write(false, "İstek eksik.");

        _log.Info("op-" + SafeCode(request.Op));
        return request.Op switch
        {
            "status" => Write(true, "", status: _coordinator.Status()),
            "enable" => From(_coordinator.EnableAsync().GetAwaiter().GetResult()),
            "update" => From(_coordinator.UpdateListAsync().GetAwaiter().GetResult()),
            "lock" => From(_coordinator.EstablishLock(request.Password ?? "", request.Confirm ?? "")),
            "disable" => From(_coordinator.DisableAsync(request.Secret).GetAwaiter().GetResult()),
            "uninstall" => From(_coordinator.UninstallAsync(request.Secret).GetAwaiter().GetResult()),
            "allow" => From(_coordinator.SetAllowlist(request.Secret, request.Domains ?? [])),
            "autoupdate" => From(_coordinator.SetAutoUpdate(request.Secret, request.Enabled ?? false)),
            "harden" => From(_coordinator.SetHardening(request.Secret, new HardeningRequest(
                request.Doh ?? false, request.SafeSearch ?? false, request.FamilyDns ?? false, request.BlockDohIps ?? false, true))),
            "search" => Write(true, "", domains: _coordinator.Search(request.Q ?? "").ToArray()),
            "maintain" => Maintain(),
            _ => Write(false, "Bilinmeyen işlem.")
        };
    }

    private string Maintain()
    {
        _coordinator.MaintainAsync().GetAwaiter().GetResult();
        return Write(true, "", status: _coordinator.Status());
    }

    private static string From(OperationResult result) =>
        Write(result.Success, result.Message, result.RecoveryKey, null, null);

    private static string Write(bool ok, string message, string? recoveryKey = null, StatusSnapshot? status = null, string[]? domains = null)
    {
        var payload = new Response
        {
            Ok = ok,
            Message = message,
            RecoveryKey = recoveryKey,
            Status = status,
            Domains = domains
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string SafeCode(string op)
    {
        foreach (var ch in op)
        {
            if (ch is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z'))
                return "unknown";
        }
        return op.Length > 32 ? "unknown" : op;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private sealed class Request
    {
        public string? Op { get; set; }
        public string? Password { get; set; }
        public string? Confirm { get; set; }
        public string? Secret { get; set; }
        public string[]? Domains { get; set; }
        public bool? Enabled { get; set; }
        public bool? Doh { get; set; }
        public bool? SafeSearch { get; set; }
        public bool? FamilyDns { get; set; }
        public bool? BlockDohIps { get; set; }
        public string? Q { get; set; }
    }

    private sealed class Response
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = "";
        public string? RecoveryKey { get; set; }
        public StatusSnapshot? Status { get; set; }
        public string[]? Domains { get; set; }
    }
}
