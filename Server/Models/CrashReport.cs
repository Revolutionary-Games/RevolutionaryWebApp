namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;
using Enums;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Notifications;
using SharedBase.Converters;
using Utilities;

[Index(nameof(CreatedAt))]
[Index(nameof(UpdatedAt))]
[Index(nameof(HappenedAt))]
[Index(nameof(HashedDeleteKey), IsUnique = true)]
[Index(nameof(UploadedFrom))]
[Index(nameof(CondensedCallstack))]
[Index(nameof(ReporterEmail))]
[Index(nameof(Description))]
public class CrashReport : UpdateableModel, IUpdateNotifications, IContainsHashedLookUps
{
    public const string CrashReportTempStorageFolderName = "crashReports";

    public bool Public { get; set; }

    public ReportState State { get; set; } = ReportState.Open;

    public ThrivePlatform Platform { get; set; }

    [AllowSortingBy]
    public DateTime HappenedAt { get; set; }

    [HashedLookUp]
    public Guid DeleteKey { get; set; } = Guid.NewGuid();

    [Required]
    public string HashedDeleteKey { get; set; } = string.Empty;

    /// <summary>
    ///   The IP address the report was uploaded from, used to combat spam / too many reports from the same user
    ///   in an hour
    /// </summary>
    [Required]
    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress UploadedFrom { get; set; } = IPAddress.None;

    [Required]
    public string ExitCodeOrSignal { get; set; } = string.Empty;

    [Required]
    public string Logs { get; set; } = string.Empty;

    public string? Store { get; set; }
    public string? Version { get; set; }

    public string? PrimaryCallstack { get; set; }
    public string? CondensedCallstack { get; set; }

    /// <summary>
    ///   The entire decoded crash dump
    /// </summary>
    public string? WholeCrashDump { get; set; }

    /// <summary>
    ///   Manually or automatically written issue description by developers
    /// </summary>
    [UpdateFromClientRequest]
    public string? Description { get; set; }

    public DateTime? DescriptionLastEdited { get; set; }

    public long? DescriptionLastEditedById { get; set; }

    public User? DescriptionLastEditedBy { get; set; }

    public long? DuplicateOfId { get; set; }

    public CrashReport? DuplicateOf { get; set; }

    public ICollection<CrashReport> Duplicates { get; set; } = new HashSet<CrashReport>();

    // TODO: switch this to a proper notification system with subscription confirmations and users also being able
    // to watch reports
    public string? ReporterEmail { get; set; }

    /// <summary>
    ///   Where this crash report's dump exists in the storage. TODO: implement the new crash uploading endpoint
    /// </summary>
    public string? UploadStoragePath { get; set; }

    [Timestamp]
    public uint DbVersion { get; set; }

    [NotMapped]
    public string? StoreOrVersion => Store ?? Version;

    public void UpdateProcessedDumpIfChanged(string newDump, string? primaryCallstack, string? condensedCallstack)
    {
        if (newDump == WholeCrashDump && PrimaryCallstack == primaryCallstack &&
            CondensedCallstack == condensedCallstack)
        {
            return;
        }

        WholeCrashDump = newDump;
        PrimaryCallstack = primaryCallstack;
        CondensedCallstack = condensedCallstack;
        this.BumpUpdatedAt();
    }

    public CrashReportInfo GetInfo()
    {
        return new()
        {
            Id = Id,
            UpdatedAt = UpdatedAt,
            CreatedAt = CreatedAt,
            State = State,
            Public = Public,
            Platform = Platform,
            HappenedAt = HappenedAt,
            ExitCodeOrSignal = ExitCodeOrSignal,
            StoreOrVersion = StoreOrVersion,
        };
    }

    public CrashReportDTO GetDTO(bool includeAnonymizedIp)
    {
        return new()
        {
            Id = Id,
            UpdatedAt = UpdatedAt,
            CreatedAt = CreatedAt,
            Public = Public,
            State = State,
            Platform = Platform,
            HappenedAt = HappenedAt,
            ExitCodeOrSignal = ExitCodeOrSignal,
            Store = Store,
            Version = Version,
            PrimaryCallstack = PrimaryCallstack,
            CondensedCallstack = CondensedCallstack,
            Description = Description,
            DescriptionLastEdited = DescriptionLastEdited,
            DescriptionLastEditedById = DescriptionLastEditedById,
            DuplicateOfId = DuplicateOfId,

            // TODO: This kind of leaks information to unauthorized users, but there isn't much they can do with this
            // Fixing would require passing the remote user access level to this method as a parameter
            CanReProcess = !string.IsNullOrEmpty(UploadStoragePath),

            AnonymizedReporterIp = includeAnonymizedIp ? IPHelpers.PartlyAnonymizedIP(UploadedFrom) : null,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        var info = GetInfo();

        if (Public)
        {
            yield return new Tuple<SerializedNotification, string>(
                new CrashReportListUpdated { Type = entityState.ToChangeType(), Item = info },
                NotificationGroups.CrashReportListUpdatedPublic);
        }
        else
        {
            yield return new Tuple<SerializedNotification, string>(
                new CrashReportListUpdated { Type = entityState.ToChangeType(), Item = info },
                NotificationGroups.CrashReportListUpdatedPrivate);
        }

        // TODO: if really necessary we'll need a separate notification group for developers to join
        yield return new Tuple<SerializedNotification, string>(
            new CrashReportUpdated { Item = GetDTO(false) },
            NotificationGroups.CrashReportUpdatedPrefix + Id);
    }
}
