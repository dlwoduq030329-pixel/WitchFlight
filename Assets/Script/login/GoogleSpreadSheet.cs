using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;
using System.Text;

public static class GoogleSpreadSheet
{
    // 본인 스프레드시트 ID와 API Key로 변경
    private const string SHEET_URL =
       "https://sheets.googleapis.com/v4/spreadsheets/" +
    "1OgSavbtnO16v6hZBWiap5uzGR-sQKK_lOiviCosIXsc" +
    "/values/Sheet1!A1:A500?key=" +
    "AIzaSyC-0qhmXJ8Rp48PPb1Apb08VUo7erGuU9Q";

    private const string GAS_WEB_APP_URL = "https://script.google.com/macros/s/AKfycbxKsT7VYUFEI_7_fG7CyFsI8j7IHd7jZAIZ8AD8b8TU7M6y4SQxNUm_7FNq9sPICKidFg/exec";

    /// <summary>
    /// A열의 빈 공간(가장 하단)에 새로운 string 데이터(ID 등)를 추가
    /// </summary>
    /// <param name="id">추가할 데이터</param>
    /// <param name="callback">성공 여부 반환</param>
    public static IEnumerator AppendID(string id, Action<bool> callback)
    {
        // 전송할 JSON 데이터 구성
        JObject jsonBody = new JObject();
        jsonBody["id"] = id;

        string jsonString = jsonBody.ToString();
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonString);

        using (UnityWebRequest request = new UnityWebRequest(GAS_WEB_APP_URL, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Google Sheet 작성 실패 : {request.error}");
                callback?.Invoke(false);
                yield break;
            }

            Debug.Log($"Google Sheet 작성 성공 : {id}");
            callback?.Invoke(true);
        }
    }
    /// <summary>
    /// ID 중복 검사
    /// true  = 사용 가능
    /// false = 이미 존재하거나 오류
    /// </summary>
    public static IEnumerator CheckID(
        string id,
        Action<bool> callback)
    {
        using (UnityWebRequest request =
               UnityWebRequest.Get(SHEET_URL))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    $"Google Sheet 조회 실패 : {request.error}"
                );

                callback?.Invoke(false);
                yield break;
            }

            JObject json =
                JObject.Parse(request.downloadHandler.text);

            JToken values = json["values"];

            // 데이터가 하나도 없으면 사용 가능
            if (values == null)
            {
                callback?.Invoke(true);
                yield break;
            }

            JArray rows = (JArray)values;

            for (int i = 0; i < rows.Count; i++)
            {
                JArray row = (JArray)rows[i];

                // A열 값이 존재하는지 확인
                if (row.Count == 0)
                    continue;

                string sheetID = row[0].ToString();

                // 중복 발견
                if (sheetID == id)
                {
                    Debug.Log($"중복된 ID : {id}");

                    callback?.Invoke(false);
                    yield break;
                }
            }

            // 끝까지 찾지 못함 = 사용 가능
            Debug.Log($"사용 가능한 ID : {id}");

            callback?.Invoke(true);
        }
    }
}