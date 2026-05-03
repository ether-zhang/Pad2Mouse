# TODO

后续待办，按需排期。

- [x] 支持开机自启控制，放入设置（注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 项）
- [x] 更换图标（当前是占位 P2M 字形）
- [ ] 主页 / 设置换成更易懂的名称
- [ ] 支持 Xbox 手柄（XInput 或 HID 枚举不同 VID/PID）
- [x] 去掉实时数据面板，腾出空间展示按键映射
- [x] 支持运行时更换按键映射（当前硬编码在 `MapperEngine`）
- [ ] GUI 优化，更直观的图片映射，更现代的界面
- [x] 不用主机徽键, 而是 L3+R3 控制开启与关闭
- [ ] 增加键盘映射
- [x] 组合键打开虚拟键盘（L1+R1 切换内置半透明 QWERTY 键盘）