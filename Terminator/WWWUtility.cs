using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

public static class WWWUtility
{
    public static bool IsEqual(byte[] x, byte[] y)
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

    public static MemoryStream MD5Read(byte[] bytes, ref byte[] md5)
    {
        var stream = new MemoryStream(bytes);
        long length = stream.Length - 16;
        stream.Position = length;
        Array.Resize(ref md5, 16);
        stream.Read(md5, 0, 16);
        stream.SetLength(length);
        stream.Position = 0L;
        return IsEqual(MD5.Create().ComputeHash(stream), md5) ? stream : null;
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
        MemoryStream stream;
        byte[] md5 = null;
        while(true)
        {
            www = form == null ? UnityWebRequest.Get(url) :  UnityWebRequest.Post(url, form);
            yield return www.SendWebRequest();
            error = www.error;
            if (string.IsNullOrEmpty(error))
            {
                try
                {
                    stream = MD5Read(www.downloadHandler.data, ref md5);
                    if (stream != null)
                    {
                        using var reader = new BinaryReader(stream);
                        
                        result = read(reader);
                    }
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
