using System;

namespace EngineContext.Editor.Extraction
{
    /// <summary>
    /// L3 내부 유틸 — 타입이 엔진/내장 패키지 소속인지, 프로젝트(또는 서드파티) 코드인지 분류.
    /// AI에게 가치가 높은 정보(커스텀 스크립트·사용자 SO)를 우선하기 위한 출력 큐레이션 근거.
    /// </summary>
    internal static class TypeClassifier
    {
        /// <summary>
        /// UnityEngine/UnityEditor/Unity.*/TMPro 네임스페이스는 엔진·내장 패키지 타입으로 본다.
        /// 네임스페이스 없음 = 사용자 코드로 간주 (인디 프로젝트에서 흔한 스타일).
        /// VContainer/Zenject 등 서드파티는 프로젝트 특징이므로 커스텀 쪽으로 분류한다.
        /// </summary>
        public static bool IsEngineType(Type type)
        {
            var ns = type.Namespace;
            if (string.IsNullOrEmpty(ns))
                return false;
            return ns == "Unity"
                   || ns.StartsWith("Unity.", StringComparison.Ordinal)
                   || ns.StartsWith("UnityEngine", StringComparison.Ordinal)
                   || ns.StartsWith("UnityEditor", StringComparison.Ordinal)
                   || ns.StartsWith("TMPro", StringComparison.Ordinal);
        }
    }
}
