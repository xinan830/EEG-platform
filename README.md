# 脑电分析平台

面向 BDF/EDF 文件的离线脑电导入、分析与可视化平台。第一阶段不连接实时硬件设备。

## 开发启动

Windows 下可直接双击根目录的 `start_brain_platform.bat`，它会分别打开后端和前端开发服务窗口。该脚本只启动开发服务，不执行构建。

当前机器的 Windows `PATH` 中存在一个非 Node.js 的 `C:\Windows\System32\npm`。
若运行 `npm --version` 显示的是 Node 版本号（例如 `v24.x`）或命令没有构建输出，先将
`D:\nodejs` 放到当前 PowerShell 会话的 `PATH` 最前面：

```powershell
$env:Path = "D:\nodejs;$env:Path"
```

```powershell
cd backend
uv sync --extra dev
uv run uvicorn app.main:app --reload --host 127.0.0.1 --port 8000

cd ../frontend
npm install
npm run dev
```

前端地址为 `http://127.0.0.1:5173`，后端 API 文档为 `http://127.0.0.1:8000/docs`。
