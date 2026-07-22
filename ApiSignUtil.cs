using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace YoloDetector
{
    // ============================================================
    // API签名工具类
    // 功能：提供API签名相关的工具方法，包括MD5加密、Token生成、Base64编解码等
    // 使用：所有方法都是静态的，直接通过类名调用，不需要创建实例
    // ============================================================
    public static class ApiSignUtil
    {
        
        // ============================================================
        // MD5加密
        // 参数：input - 要加密的字符串
        // 返回：32位小写MD5加密结果
        // ============================================================
        public static string Md5(string input)
        {
            // 创建MD5加密对象
            using (MD5 md5 = MD5.Create())
            {
                // 将字符串转换为UTF-8字节数组
                byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                
                // 将字节数组转换为十六进制字符串
                StringBuilder result = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    // 格式化为两位十六进制，小写
                    result.Append(b.ToString("x2"));
                }
                
                // 返回加密结果
                return result.ToString();
            }
        }
        
        // ============================================================
        // 生成签名Token（核心方法）
        // 签名流程：
        // 1. 在请求参数中添加时间戳t
        // 2. 添加秘钥secret
        // 3. 对所有参数key按字典序排序
        // 4. 按 key1=value1&key2=value2&...&secret=秘钥 格式拼接
        // 5. 对拼接后的字符串进行MD5加密，得到token
        // 
        // 参数：
        //   paramsDict - 请求参数字典
        //   secret - 签名秘钥（可选，默认从配置文件读取）
        // 返回：生成的签名Token
        // ============================================================
        public static string GenerateToken(Dictionary<string, string> paramsDict, string secret = null)
        {
            // 如果没有传入秘钥，从配置文件读取默认秘钥
            string actualSecret = string.IsNullOrEmpty(secret)
                ? AppConfig.Current.Api.SignSecret
                : secret;
            
            // 复制参数字典（避免修改原字典）
            Dictionary<string, string> signParams = new Dictionary<string, string>(paramsDict);
            
            // 添加时间戳（Unix时间戳，单位秒）
            signParams["t"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            
            // 获取所有参数key，并添加secret
            List<string> keys = new List<string>(signParams.Keys);
            keys.Add("secret");
            
            // 按字典序排序
            keys.Sort();
            
            // 拼接参数字符串
            StringBuilder sb = new StringBuilder();
            foreach (string key in keys)
            {
                // 跳过token参数（如果有的话）
                if (string.Equals(key, "token", StringComparison.OrdinalIgnoreCase))
                    continue;
                
                // 如果不是第一个参数，添加&分隔符
                if (sb.Length > 0)
                    sb.Append("&");
                
                // 添加参数
                if (string.Equals(key, "secret", StringComparison.OrdinalIgnoreCase))
                {
                    // secret参数使用实际的秘钥值
                    sb.Append(key + "=" + actualSecret);
                }
                else
                {
                    // 其他参数使用参数字典中的值
                    sb.Append(key + "=" + signParams[key]);
                }
            }
            
            // 对拼接后的字符串进行MD5加密
            string signStr = sb.ToString();
            return Md5(signStr);
        }
        
        // ============================================================
        // 生成带签名的参数字典
        // 参数：paramsDict - 原始请求参数
        // 返回：包含token和时间戳的新参数字典
        // ============================================================
        public static Dictionary<string, string> GenerateSignParams(Dictionary<string, string> paramsDict)
        {
            // 复制原始参数
            Dictionary<string, string> signParams = new Dictionary<string, string>(paramsDict);
            
            // 生成token
            string token = GenerateToken(paramsDict);
            
            // 添加token和时间戳
            signParams["token"] = token;
            signParams["t"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            
            // 返回带签名的参数
            return signParams;
        }
        
        // ============================================================
        // 将参数字典转换为URL查询字符串
        // 参数：paramsDict - 参数字典
        // 返回：URL查询字符串（如"key1=value1&key2=value2"）
        // ============================================================
        public static string ToQueryString(Dictionary<string, string> paramsDict)
        {
            List<string> parts = new List<string>();
            
            // 遍历参数字典
            foreach (KeyValuePair<string, string> pair in paramsDict)
            {
                // 对值进行URL编码（处理特殊字符）
                parts.Add(pair.Key + "=" + Uri.EscapeDataString(pair.Value));
            }
            
            // 用&连接所有参数
            return string.Join("&", parts);
        }
        
        // ============================================================
        // Base64编码
        // 参数：plainText - 要编码的普通字符串
        // 返回：Base64编码后的字符串
        // ============================================================
        public static string Base64Encode(string plainText)
        {
            // 将字符串转换为UTF-8字节数组
            byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            
            // 进行Base64编码
            return Convert.ToBase64String(plainTextBytes);
        }
        
        // ============================================================
        // Base64解码
        // 参数：base64EncodedData - Base64编码的字符串
        // 返回：解码后的普通字符串
        // ============================================================
        public static string Base64Decode(string base64EncodedData)
        {
            // 进行Base64解码
            byte[] base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
            
            // 将字节数组转换为UTF-8字符串
            return Encoding.UTF8.GetString(base64EncodedBytes);
        }
    }
}