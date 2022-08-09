namespace ThriveDevCenter.Server.Tests.Fixtures;

using System;
using System.Collections.Generic;
using Moq;
using Server.Models;
using Server.Services;
using Shared;

public class SampleCLADatabase : BaseSharedDatabaseFixtureWithNotifications
{
    private static readonly object Lock = new object();
    private static bool databaseInitialized;

    public SampleCLADatabase() : base(new Mock<IModelUpdateNotificationSender>().Object, "SampleCLADatabase")
    {
        lock (Lock)
        {
            if (!databaseInitialized)
            {
                Seed();
                databaseInitialized = true;
            }
        }
    }

    public long CLA1Id => 1;
    public long CLA2Id => 2;

    public string CLA1Signature1Email => "user@example.com";
    public string CLA1Signature1Github => "GithubUserName";

    public string CLA2Signature1Email => "user2@example.com";
    public string CLA2Signature1DeveloperUsername => "Dev2";
    public string CLA2Signature1Github => "GithubUserName2";

    public string CLA2Signature2Email => "user3@example.com";
    public string CLA2Signature2Github => "GithubUserName";

    protected sealed override void Seed()
    {
        var cla1Signature1 = new ClaSignature()
        {
            Email = CLA1Signature1Email,
            ClaSignatureStoragePath = "cla1/signature1",
            DeveloperUsername = "Dev1",
            GithubAccount = CLA1Signature1Github,
        };

        var cla1 = new Cla()
        {
            Active = false,
            Id = CLA1Id,
            RawMarkdown = "CLA 1",
            Signatures = new HashSet<ClaSignature>()
            {
                cla1Signature1,
            },
        };

        cla1Signature1.Cla = cla1;

        var cla2Signature1 = new ClaSignature()
        {
            Email = CLA2Signature1Email,
            ClaSignatureStoragePath = "cla2/signature1",
            DeveloperUsername = CLA2Signature1DeveloperUsername,
            GithubAccount = CLA2Signature1Github,
        };

        var cla2Signature2 = new ClaSignature()
        {
            Email = CLA2Signature2Email,
            ClaSignatureStoragePath = "cla2/signature2",
            GithubAccount = CLA2Signature2Github,
        };

        var cla2 = new Cla()
        {
            Active = true,
            Id = CLA2Id,
            RawMarkdown = "CLA 2",
            Signatures = new HashSet<ClaSignature>()
            {
                cla2Signature1,
                cla2Signature2,
            },
        };

        cla2Signature1.Cla = cla2;
        cla2Signature2.Cla = cla2;

        Database.Clas.Add(cla1);
        Database.ClaSignatures.Add(cla1Signature1);

        Database.Clas.Add(cla2);
        Database.ClaSignatures.Add(cla2Signature1);
        Database.ClaSignatures.Add(cla2Signature2);

        if (CLA1Signature1Github.Length <= AppInfo.PartialGithubMatchRevealAfterLenght)
            throw new Exception("configured github name is too short");

        if (CLA2Signature2Email.Length <= AppInfo.PartialEmailMatchRevealAfterLenght)
            throw new Exception("configured email is too short");

        Database.SaveChanges();
    }
}