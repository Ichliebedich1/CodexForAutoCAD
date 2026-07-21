using System;
using System.Collections.Generic;
using System.Linq;

namespace Codex.AutoCAD.Contracts.Adversarial.Specs;

public static class Program
{
    public static int Main()
    {
        var specs = new SpecCase[]
        {
            new("ADV-V2-001 xorshift32固定种子打乱实体顺序保持canonical确定性", AdvV2001_XorShift32ShufflePreservesCanonical.Run),
            new("ADV-V2-002 重复Handle精确包含context_v2_handle_duplicate", AdvV2002_DuplicateHandleRejected.Run),
            new("ADV-V2-003 中文emoji换行组合Unicode保持确定性", AdvV2003_UnicodeDeterminism.Run),
            new("ADV-V2-004 控制字符注入精确结构化失败码", AdvV2004_ControlCharInjectionRejected.Run),
            new("ADV-V2-005 双向格式零宽字符代理项稳定拒绝", AdvV2005_BidiAndSurrogateRejected.Run),
            new("ADV-V2-006 规范JSON超256KiB精确包含context_v2_json_bytes_limit", AdvV2006_JsonBytesLimitEnforced.Run),
            new("ADV-V2-007 null/零payload精确shape/entity错误码", AdvV2007_NullPayloadRejected.Run),
            new("ADV-V2-008 不一致状态精确拒绝", AdvV2008_InconsistentStateRejected.Run),
            new("ADV-V2-009 反射DTO确认无隐私字段", AdvV2009_NoPrivacyFieldsInDto.Run),
            new("ADV-V2-010 256轮压力测试aggregate hash", AdvV2010_256RoundStressTest.Run),
        };

        var failed = 0;
        foreach (var spec in specs)
        {
            try
            {
                spec.Run();
                Console.WriteLine("PASS " + spec.Name);
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine("FAIL " + spec.Name + ": " + exception.Message);
            }
        }

        Console.WriteLine($"{specs.Length - failed}/{specs.Length} adversarial specs passed");
        return failed == 0 ? 0 : 1;
    }

    private sealed class SpecCase
    {
        public SpecCase(string name, Action run)
        {
            Name = name;
            Run = run;
        }

        public string Name { get; }
        public Action Run { get; }
    }
}
