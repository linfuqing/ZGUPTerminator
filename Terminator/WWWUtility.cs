using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

public static class WWWUtility
{
    public static bool IsEqual(in ReadOnlySpan<byte> x, in ReadOnlySpan<byte> y)
    {
        int length = x.Length;
        if (length != y.Length)
            return false;

        for (int i = 0; i < length; ++i)
        {
            if (x[i] != y[i])
                return false;
        }

        return true;
    }

    public static string ToMD5(string input)
    {
        var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        bytes = md5.ComputeHash(bytes);
        string result = BitConverter.ToString(bytes);
        result = result.Replace("-", "");
        //result = result.ToLower();
        return result;
    }

    public static bool MD5Vail(byte[] bytes)
    {
        int length = bytes.Length - 16;
        return IsEqual(MD5.Create().ComputeHash(bytes, 0, length), bytes.AsSpan(length, 16));
    }

    public static IEnumerator MD5Request(
        Predicate<BinaryReader> read, 
        WWWForm form, 
        string url, 
        float interval = 1.0f)
    {
        bool result = false;
        string error;
        UnityWebRequest www;
        byte[] bytes;
        while(true)
        {
            www = form == null ? UnityWebRequest.Get(url) :  UnityWebRequest.Post(url, form);
            yield return www.SendWebRequest();
            error = www.error;
            if (string.IsNullOrEmpty(error))
            {
                try
                {
                    bytes = www.downloadHandler.data;
                    if(MD5Vail(bytes))
                    {
                        using var reader = new BinaryReader(new MemoryStream(bytes, 0, bytes.Length - 16, false ));
                        
                        result = read(reader);
                    }
                    else
                        Debug.LogError(www.downloadHandler.text);
                }
                catch (Exception e)
                {
                    Debug.LogException(e.InnerException ?? e);
                }
            }

            if (result)
                break;

            yield return new WaitForSecondsRealtime(interval);
        }
    }
}
