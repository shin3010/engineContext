using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace EngineContext.Editor.IO
{
    /// <summary>
    /// L5 IO — 프로젝트 루트 경로 해석, 기존 파일 확인, CLAUDE.md 기록 (TA §3, FS F6).
    /// 실패 시 파일을 변경하지 않으며 초안은 호출 측(Window)이 보존한다 (E6).
    /// 이 클래스가 이 도구가 디스크에 쓰는 유일한 지점이다 (읽기 전용 불변 계약).
    /// </summary>
    public class ContextFileWriter
    {
        public const string FileName = "CLAUDE.md";

        public struct WriteResult
        {
            public bool success;
            public string path;
            public string error;
        }

        /// <summary>프로젝트 루트 = Assets의 부모 디렉터리. 위치는 항상 고정 (UX §7.2).</summary>
        public string ResolveRootPath()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        public string ResolveFilePath()
        {
            return Path.Combine(ResolveRootPath(), FileName);
        }

        /// <summary>E7: 기존 파일 존재 여부 — 덮어쓰기 1회 확인용.</summary>
        public bool RootFileExists()
        {
            return File.Exists(ResolveFilePath());
        }

        /// <summary>
        /// 루트 CLAUDE.md를 읽는다(Prompt Builder의 단일 진입 지점). 재추출하지 않고 기존 산출물을 재사용.
        /// 성공 시 content 반환·true, 부재/실패 시 error 반환·false.
        /// </summary>
        public bool TryReadProjectContext(out string content, out string error)
        {
            content = null;
            error = null;
            var path = ResolveFilePath();
            if (!File.Exists(path))
            {
                error = "먼저 Generate Context를 실행해 CLAUDE.md를 만들어 주세요.";
                return false;
            }
            try
            {
                content = File.ReadAllText(path);
                return true;
            }
            catch (Exception ex)
            {
                error = "CLAUDE.md를 읽지 못했습니다. 파일이 존재하고 접근 가능한지 확인해 주세요. (" + ex.Message + ")";
                return false;
            }
        }

        public WriteResult Write(string draft)
        {
            var path = ResolveFilePath();
            try
            {
                File.WriteAllText(path, draft, new UTF8Encoding(false)); // BOM 없는 UTF-8
                return new WriteResult { success = true, path = path };
            }
            catch (Exception ex)
            {
                // E6: 권한/잠금/읽기전용 → 파일 미변경, 오류 반환
                return new WriteResult { success = false, path = path, error = ex.Message };
            }
        }
    }
}
