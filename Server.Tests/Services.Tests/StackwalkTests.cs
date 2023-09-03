namespace ThriveDevCenter.Server.Tests.Services.Tests;

using System.Net.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Server.Services;
using Xunit;

public class StackwalkTests
{
    [Fact]
    public void Stackwalk_CallstackCondenseWorks()
    {
        var clientFactoryMock = Substitute.For<IHttpClientFactory>();
        var stackwalk = new Stackwalk(new ConfigurationBuilder().Build(), clientFactoryMock);

        // ReSharper disable StringLiteralTypo
        var result = stackwalk.CondenseCallstack(@"Thread 0 (crashed)
 0  Thrive + 0x922fd6
    rax = 0x0000000000000000   rdx = 0x000000000016c7f0
    rcx = 0x0000000000000ad9   rbx = 0x00007ffdf248fe80
    rsi = 0x00000000000428e9   rdi = 0x0000000000000001
    rbp = 0x00000000038a3740   rsp = 0x00007ffdf248fe70
     r8 = 0x0000000000000000    r9 = 0x0000000000000001
    r10 = 0x00007ffdf25ed170   r11 = 0x00007ffdf25ed1b0
    r12 = 0x00007ffdf2490798   r13 = 0x00007ffdf248fe80
    r14 = 0x00000000038a3740   r15 = 0x00000000029c6d00
    rip = 0x0000000000d22fd6
    Found by: given as instruction pointer in context
 1  Thrive + 0x5f830e
    rsp = 0x00007ffdf248fe80   rip = 0x00000000009f830e
    Found by: stack scanning
 2  ld-linux-x86-64.so.2 + 0xaa00
    rsp = 0x00007ffdf248fef0   rip = 0x00007f085d5d7a00
    Found by: stack scanning
 3  Thrive + 0x1b80c10
    rsp = 0x00007ffdf248ff00   rip = 0x0000000001f80c10
    Found by: stack scanning
");

        Assert.Equal(@" 0  Thrive + 0x922fd6
 1  Thrive + 0x5f830e
 2  ld-linux-x86-64.so.2 + 0xaa00
 3  Thrive + 0x1b80c10
", result);

        // ReSharper restore StringLiteralTypo
    }

    [Fact]
    public void Stackwalk_NoFramesCondenseWorks()
    {
        var clientFactoryMock = Substitute.For<IHttpClientFactory>();
        var stackwalk = new Stackwalk(new ConfigurationBuilder().Build(), clientFactoryMock);

        // ReSharper disable StringLiteralTypo
        var result = stackwalk.CondenseCallstack(@"Thread 25 (crashed)
 <no frames>
");

        Assert.Equal(@" <no frames>
", result);

        // ReSharper restore StringLiteralTypo
    }
}
