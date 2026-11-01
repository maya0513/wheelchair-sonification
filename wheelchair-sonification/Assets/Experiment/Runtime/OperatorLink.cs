using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace WheelchairSonification.Runtime
{
    public sealed class OperatorLink : MonoBehaviour
    {
        public ResearchVisitHost Host;
        public string PairingCode { get; private set; }
        public bool Paired => !string.IsNullOrEmpty(token);
        public string Status { get; private set; } = "PCとの接続を待っています。";
        public string EndpointOverride { get; set; }
        private string deviceId,
            token,
            endpoint;
        private bool active,
            uploading;

        [Serializable]
        private sealed class Hello
        {
            public int schema = 3;
            public string deviceId,
                codeHash;
        }

        [Serializable]
        private sealed class Reply
        {
            public int schema;
            public string pairToken;
            public bool operatorConnected;
            public double operatorLeaseSeconds;
            public OperatorCommand command;
        }

        [Serializable]
        private sealed class Exchange
        {
            public OperatorSnapshot state;
            public OperatorResult result;
        }

        [Serializable]
        private sealed class UploadReply
        {
            public bool saved;
            public string sha256;
        }

        public void Begin()
        {
            if (active)
                return;
            deviceId = Guid.NewGuid().ToString("N");
            byte[] bytes = new byte[4];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            PairingCode = (BitConverter.ToUInt32(bytes, 0) % 1000000).ToString("000000");
            active = true;
            StartCoroutine(Connect());
        }

        private IEnumerator Connect()
        {
            OperatorResult lastResult = null;
            while (active)
            {
                string next = EndpointOverride ?? AndroidLanGateway.Endpoint();
                if (next != endpoint)
                {
                    endpoint = next;
                    token = null;
                }
                if (endpoint == null)
                {
                    Status = "PCのホットスポットへ接続してください。";
                    yield return new WaitForSecondsRealtime(.5f);
                    continue;
                }
                bool reportedStopped = Paired && !Host.Research.Active;
                string body = Paired
                    ? JsonUtility.ToJson(
                        new Exchange { state = Host.Snapshot(), result = lastResult }
                    )
                    : JsonUtility.ToJson(
                        new Hello
                        {
                            deviceId = deviceId,
                            codeHash = Hash(deviceId + ":" + PairingCode),
                        }
                    );
                using (
                    var request = Json(
                        endpoint + (Paired ? "/device/exchange" : "/device/hello"),
                        body
                    )
                )
                {
                    if (Paired)
                        request.SetRequestHeader("X-Device-Token", token);
                    double requestStart = Time.realtimeSinceStartupAsDouble;
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        Reply reply = null;
                        try
                        {
                            reply = JsonUtility.FromJson<Reply>(request.downloadHandler.text);
                        }
                        catch (Exception e)
                        {
                            Status = "PC応答の形式を確認できません：" + e.Message;
                        }
                        if (reply != null)
                        {
                            if (reply.schema != 3)
                            {
                                Status =
                                    "通信形式が異なります。対応するAPKとコンソールを使ってください。";
                                if (Host.Research.Active)
                                    Host.Suspend("operator_protocol_mismatch");
                                active = false;
                                yield break;
                            }
                            if (!string.IsNullOrEmpty(reply.pairToken))
                                token = reply.pairToken;
                            Host.ObserveOperator(
                                reply.operatorConnected,
                                Math.Max(
                                    0,
                                    reply.operatorLeaseSeconds
                                        - (Time.realtimeSinceStartupAsDouble - requestStart)
                                )
                            );
                            if (reply.command != null && !string.IsNullOrEmpty(reply.command.id))
                                lastResult = Host.Execute(reply.command);
                            Status = Paired
                                ? "実施者に接続済み"
                                : "PCで確認番号を入力してください。";
                        }
                    }
                    else
                    {
                        Status =
                            request.responseCode == 409
                                ? "PCとアプリの通信形式が異なります。同じ版のコンソールを使ってください。"
                                : "PCとの通信を確認できません。";
                        if (request.responseCode == 401)
                            token = null;
                    }
                }
                if (
                    Host.ExportPath != null
                    && !uploading
                    && Paired
                    && reportedStopped
                    && !Host.Research.Active
                )
                    StartCoroutine(Upload(Host.ExportPath));
                yield return new WaitForSecondsRealtime(.5f);
            }
        }

        private IEnumerator Upload(string path)
        {
            if (!Host.BeginTransfer())
                yield break;
            uploading = true;
            try
            {
                string digest = null;
                try
                {
                    using var input = File.OpenRead(path);
                    using var sha = SHA256.Create();
                    digest = Hex(sha.ComputeHash(input));
                }
                catch (Exception e)
                {
                    Status = "ZIP読込み失敗。元記録は保持：" + e.Message;
                }
                if (digest == null)
                    yield break;
                using var request = new UnityWebRequest(endpoint + "/device/export", "PUT");
                request.uploadHandler = new UploadHandlerFile(path);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 120;
                request.SetRequestHeader("Content-Type", "application/zip");
                request.SetRequestHeader("X-Device-Token", token);
                request.SetRequestHeader("X-Content-SHA256", digest);
                request.SetRequestHeader("X-Export-Name", Path.GetFileName(path));
                yield return request.SendWebRequest();
                if (
                    request.result == UnityWebRequest.Result.Success
                    && ReceiptMatches(request.downloadHandler.text, digest)
                )
                {
                    Host.TransferFinished(true, "sha256_verified");
                    Status = "PCへ回収・チェックサム照合済み。元記録は保持。";
                }
                else
                    Status = "PCへの回収・照合に失敗しました。元記録を保持し、再回収できます。";
            }
            finally
            {
                uploading = false;
                // Includes malformed receipts, interrupted coroutines and file-read failures.
                if (Host != null && Host.ExportPath == path)
                    Host.TransferFinished(false, Status);
            }
        }

        public static bool ReceiptMatches(string json, string digest)
        {
            try
            {
                var receipt = JsonUtility.FromJson<UploadReply>(json);
                return receipt != null
                    && receipt.saved
                    && receipt.sha256 == digest
                    && !string.IsNullOrEmpty(digest);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static UnityWebRequest Json(string url, string body)
        {
            var request = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 2,
            };
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }

        private static string Hash(string text)
        {
            using var sha = SHA256.Create();
            return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        private static string Hex(byte[] bytes) =>
            BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

        private void OnDisable()
        {
            active = false;
            StopAllCoroutines();
            if (Host != null)
                Host.Suspend("operator_link_disabled");
        }
    }
}
