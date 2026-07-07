using NUnit.Framework;
using UnityEngine;
using EngineContext.Editor.Model;

namespace EngineContext.Editor.Tests
{
    /// <summary>TA §7-2 검증: Snapshot(IR)이 JSON으로 직렬화·역직렬화 가능하고 안정적인지.</summary>
    public class SnapshotSerializationTests
    {
        [Test]
        public void EmptySnapshot_JsonRoundtrip_IsStable()
        {
            var snapshot = new Snapshot();
            var json = JsonUtility.ToJson(snapshot);
            Assert.IsNotEmpty(json);

            var restored = JsonUtility.FromJson<Snapshot>(json);
            Assert.IsNotNull(restored);
            Assert.AreEqual(json, JsonUtility.ToJson(restored), "직렬화 왕복 후 JSON이 달라지면 IR이 안정적이지 않다.");
        }

        [Test]
        public void PopulatedSnapshot_JsonRoundtrip_IsStable()
        {
            var json = JsonUtility.ToJson(SampleSnapshots.Create());
            var restored = JsonUtility.FromJson<Snapshot>(json);
            Assert.AreEqual(json, JsonUtility.ToJson(restored));
        }

        [Test]
        public void EquivalentSnapshots_SerializeIdentically()
        {
            // 동일 상태 → 동일 IR (결정론, FS F3)
            var a = JsonUtility.ToJson(SampleSnapshots.Create());
            var b = JsonUtility.ToJson(SampleSnapshots.Create());
            Assert.AreEqual(a, b);
        }
    }
}
