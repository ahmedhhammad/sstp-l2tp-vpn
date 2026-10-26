# Manual verification checklist

Run on Windows 10 or 11 x64 with administrator access, a test SSTP/L2TP server, valid test credentials, and a reachable host behind the VPN. Use the managed WPF application from Visual Studio or `dist\<version>\SarmadiVpn-win-x64.exe`. Record the Windows version, protocol, server certificate result, and outcome. Never put real passwords or PSKs in screenshots, issues, or logs.

1. **Start the app**
   - Launch the app and accept the administrator prompt.
   - Expected: the SARMADI VPN window opens and shows Disconnected with a red status indicator.

2. **Connect using SSTP**
   - Enter a DNS server name matching the SSTP certificate, username, and password; select SSTP and Connect.
   - Expected: status progresses through Connecting to Connected. A server certificate failure displays its RAS code and certificate guidance. Invalid credentials display the Windows authentication error.

3. **Connect using L2TP/IPsec**
   - Disconnect, select L2TP / IPsec, enter the pre-shared key and user credentials, and connect.
   - Expected: Windows RAS negotiates L2TP/IPsec; a PSK or server negotiation failure displays its RAS error. When behind NAT, use **Enable L2TP behind NAT** and reboot Windows before retrying.

4. **Add a split route**
   - Add `192.168.1.0/24`, connect, and inspect `route print` in an elevated terminal.
   - Expected: the route exists with mask `255.255.255.0` on the discovered VPN interface. The activity log shows the selected interface and gateway/on-link target; the route row says Applied.

5. **Reach a device through the route**
   - Ping a reachable device in `192.168.1.0/24`.
   - Expected: replies arrive through the VPN. If the route exists but ping fails, check remote routing, the device firewall, and ICMP policy.

6. **Disconnect and remove routes**
   - Choose Disconnect and inspect `route print`.
   - Expected: app-added routes are deleted, the route row returns to Not applied, and the taskbar status badge turns red.

7. **Connection state indicators**
   - Connect, disconnect, and observe the app and taskbar icons.
   - Expected: green while connected, red while disconnected/error, and yellow while connecting or disconnecting. The small round badge appears at the upper-left corner of the taskbar app icon.

8. **Close while connected**
   - Connect with an enabled route, then close the app normally.
   - Expected: the app attempts to disconnect and removes recorded app-added routes before exiting.

9. **Recover after forced termination**
   - Connect with an enabled route, terminate the app from Task Manager, relaunch, then inspect `route print` and the activity log.
   - Expected: startup reads the applied-route record and attempts to remove stale app routes.

10. **Route persistence and deletion**
    - Add a labeled route, close/reopen the app, then select and delete the route.
    - Expected: it remains listed after restart until Delete is clicked. Deletion removes it from the UI and `routes.json`, and attempts route-table cleanup immediately.

11. **Input validation**
    - Try an invalid IPv4 address, invalid prefix, duplicate route, and `0.0.0.0/0`; then enter `192.168.1.77/24`.
    - Expected: invalid, duplicate, and default routes are rejected; the host address is normalized to `192.168.1.0/24`.
