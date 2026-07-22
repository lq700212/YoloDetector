namespace YoloDetector
{
    // ============================================================
    // 相机API工厂类
    // ============================================================
    // 功能：根据配置文件中的CameraBrand字段，创建对应的ICameraApi实现类
    // 
    // 设计说明：
    //   - 使用简单工厂模式（Simple Factory Pattern）
    //   - 根据配置中的品牌标识，返回对应的实现类实例
    //   - 新增品牌时，只需：
    //     1. 创建新的ICameraApi实现类（如HikCameraApiClient）
    //     2. 在Create方法中添加对应的case分支
    //     3. 修改配置文件中的CameraBrand字段
    //   - 不需要修改MainForm等调用代码
    //
    // 使用方式：
    //   ICameraApi cameraApi = CameraApiFactory.Create(ip);
    // ============================================================
    public static class CameraApiFactory
    {
        // ============================================================
        // 根据配置创建相机API实例
        // 参数：ip - 相机IP地址
        // 返回：ICameraApi接口实例（具体类型由配置决定）
        // ============================================================
        public static ICameraApi Create(string ip)
        {
            // 获取配置中的相机品牌
            string brand = AppConfig.Current.Api.CameraBrand.ToUpper();
            
            // 根据品牌创建对应的实现类
            switch (brand)
            {
                case "ANGEHUA":
                    // ANGEHUA（安格华）相机 - 通过RTSP协议接入
                    return new ANGEHUACameraApiClient(ip);
                
                case "HIK":
                    // HIK（海康威视）相机（示例，尚未实现）
                    // return new HikCameraApiClient(ip);
                    // 暂不支持，返回默认实现（ANGEHUA）
                    System.Diagnostics.Debug.WriteLine("HIK品牌尚未实现，使用默认实现(ANGEHUA)");
                    return new ANGEHUACameraApiClient(ip);
                
                case "DAHUA":
                    // DAHUA（大华）相机（示例，尚未实现）
                    // return new DahuaCameraApiClient(ip);
                    // 暂不支持，返回默认实现（ANGEHUA）
                    System.Diagnostics.Debug.WriteLine("DAHUA品牌尚未实现，使用默认实现(ANGEHUA)");
                    return new ANGEHUACameraApiClient(ip);
                
                default:
                    // 未知品牌，返回默认实现（ANGEHUA）
                    // 输出警告到调试日志
                    System.Diagnostics.Debug.WriteLine("未知相机品牌: " + brand + ", 使用默认实现(ANGEHUA)");
                    return new ANGEHUACameraApiClient(ip);
            }
        }
        
        // ============================================================
        // 获取当前支持的相机品牌列表
        // 返回：品牌标识数组
        // ============================================================
        public static string[] GetSupportedBrands()
        {
            return new[] { "ANGEHUA", "HIK", "DAHUA" };
        }
        
        // ============================================================
        // 检查品牌是否支持
        // 参数：brand - 品牌标识
        // 返回：是否支持
        // ============================================================
        public static bool IsBrandSupported(string brand)
        {
            return System.Array.Exists(GetSupportedBrands(), b => b.Equals(brand, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
