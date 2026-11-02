using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace BikiniFriends
{
    internal sealed class ChatMessage
    {
        internal string Role, Content;
        internal ChatMessage(string role, string content) { Role = role; Content = content; }
    }

    internal enum ProbeResult { Ok, ModelMissing, Offline }

    /// <summary>진행 중인 요청을 다른 스레드에서 끊을 수 있게 붙잡아 둔다(말 도중에 새 말을 걸 때).</summary>
    internal sealed class ChatRequest
    {
        private HttpWebRequest request;
        internal volatile bool Cancelled;
        internal void Attach(HttpWebRequest r) { request = r; if (Cancelled) Abort(); }
        internal void Abort() { Cancelled = true; try { if (request != null) request.Abort(); } catch { } }
    }

    /// <summary>Ollama(http://localhost:11434) 와의 통신. 모두 호출한 스레드에서 막혀 기다리므로 일꾼 스레드에서 부른다.</summary>
    internal static class OllamaClient
    {
        private static HttpWebRequest Make(ChatOptions o, string path, string method, int timeoutMs)
        {
            var r = (HttpWebRequest)WebRequest.Create(o.Endpoint.TrimEnd('/') + path);
            r.Method = method; r.Timeout = timeoutMs; r.ReadWriteTimeout = 45000; r.Proxy = null; r.UserAgent = "BikiniFriends-DeskPet";
            return r;
        }

        private static void WriteBody(HttpWebRequest r, string json)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            r.ContentType = "application/json"; r.ContentLength = bytes.Length;
            using (Stream s = r.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
        }

        /// <summary>서버가 켜져 있는지, 모델이 설치돼 있는지 본다.</summary>
        internal static ProbeResult Probe(ChatOptions o)
        {
            try
            {
                var r = Make(o, "/api/tags", "GET", 6000);
                using (var resp = (HttpWebResponse)r.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    var models = Json.Get(Json.Parse(sr.ReadToEnd()), "models") as List<object>;
                    if (models == null) return ProbeResult.Ok;
                    string want = o.Model.ToLowerInvariant(); string wantBase = want.Contains(":") ? want : want + ":latest";
                    foreach (object m in models)
                    {
                        string name = (Json.Str(m, "name") ?? Json.Str(m, "model") ?? "").ToLowerInvariant();
                        if (name == want || name == wantBase) return ProbeResult.Ok;
                    }
                    return ProbeResult.ModelMissing;
                }
            }
            catch { return ProbeResult.Offline; }
        }

        /// <summary>모델을 메모리에 미리 올려 둔다(첫 말이 느리지 않게). 결과는 기다리지 않아도 된다.</summary>
        internal static void Warmup(ChatOptions o)
        {
            try
            {
                var r = Make(o, "/api/generate", "POST", 60000);
                WriteBody(r, "{\"model\":" + Json.Quote(o.Model) + ",\"keep_alive\":" + Json.Quote(o.KeepAlive) + ",\"options\":{\"num_ctx\":3072}}");
                using (r.GetResponse()) { }
            }
            catch { }
        }

        private static string Body(ChatOptions o, List<ChatMessage> msgs, bool stream, int numPredict, double temperature)
        {
            var b = new StringBuilder("{\"model\":" + Json.Quote(o.Model) + ",\"stream\":" + (stream ? "true" : "false") + ",\"think\":false,\"keep_alive\":" + Json.Quote(o.KeepAlive));
            b.Append(",\"options\":{\"num_predict\":" + numPredict + ",\"temperature\":" + temperature.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)
                + ",\"top_p\":0.9,\"repeat_penalty\":1.12,\"num_ctx\":3072,\"stop\":[\"\\n\",\"사용자:\",\"스폰지밥:\",\"뚱이:\",\"징징이:\",\"다람이:\",\"집게사장:\",\"플랑크톤:\"]}");
            b.Append(",\"messages\":[");
            for (int i = 0; i < msgs.Count; i++)
            { if (i > 0) b.Append(','); b.Append("{\"role\":" + Json.Quote(msgs[i].Role) + ",\"content\":" + Json.Quote(msgs[i].Content) + "}"); }
            return b.Append("]}").ToString();
        }

        /// <summary>토큰이 올 때마다 onToken 을 부른다(일꾼 스레드). 성공하면 null, 실패하면 이유를 돌려준다.</summary>
        internal static string Stream(ChatOptions o, List<ChatMessage> msgs, Action<string> onToken, ChatRequest handle, int numPredict = 170, double temperature = .85)
        {
            try
            {
                var r = Make(o, "/api/chat", "POST", 60000);
                handle.Attach(r);
                WriteBody(r, Body(o, msgs, true, numPredict, temperature));
                using (var resp = (HttpWebResponse)r.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (handle.Cancelled) return "cancelled";
                        if (line.Trim().Length == 0) continue;
                        object chunk = Json.Parse(line);
                        string token = Json.Str(Json.Get(chunk, "message"), "content");
                        if (!string.IsNullOrEmpty(token)) onToken(token);
                        if (Json.Flag(chunk, "done")) break;
                    }
                }
                return null;
            }
            catch (WebException ex)
            {
                if (handle.Cancelled) return "cancelled";
                return ex.Status == WebExceptionStatus.Timeout ? "시간 초과" : ex.Message;
            }
            catch (Exception ex) { return handle.Cancelled ? "cancelled" : ex.Message; }
        }

        /// <summary>기억 추출·일기 쓰기처럼 스트리밍 없이 끝까지 한 번에 받는 요청.</summary>
        internal static string Complete(ChatOptions o, List<ChatMessage> msgs, int numPredict, double temperature)
        {
            try
            {
                var r = Make(o, "/api/chat", "POST", 45000);
                WriteBody(r, Body(o, msgs, false, numPredict, temperature));
                using (var resp = (HttpWebResponse)r.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    object res = Json.Parse(sr.ReadToEnd());
                    return Json.Str(Json.Get(res, "message"), "content") ?? "";
                }
            }
            catch { return null; }
        }
    }
}