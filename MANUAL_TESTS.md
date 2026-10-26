# Manual test checklist

Run these checks on Windows 7 SP1, Windows 10, and Windows 11 where available, with administrator access and working VPN server details. Launch `native\bin\Release\SarmadiVpn.exe` (or run the Debug x64 target from Visual Studio) and record the Windows build, VPN server type, and result for each protocol.

1. **Connect with SSTP**
   - Select SSTP, enter the server, username, and password, then choose **Connect**.
   - Expected: state changes through Connecting to Connected; the log shows the Windows RAS profile setup and connection state. A bad password, unreachable server, or SSTP certificate problem displays its RAS error code and Windows message.

2. **Connect with L2TP/IPsec**
   - Disconnect, choose L2TP / IPsec, enter the PSK and user credentials, and connect.
   - Expected: the Windows RAS profile uses L2TP with MS-CHAPv2 and split tunneling. A PSK negotiation failure is reported with the RAS error code and Windows message.

3. **Add a split route**
   - Add `192.168.1.0/24`, connect, and run `route print` in an elevated terminal.
   - Expected: the normalized route is present on the VPN interface with mask `255.255.255.0`; the app reports its discovered gateway and interface index and marks the route Applied.

4. **Reach a device through the route**
   - Ping a reachable device in `192.168.1.0/24`.
   - Expected: replies arrive through the VPN. A failed ping can also indicate remote routing, firewall, or device ICMP policy problems.

5. **Disconnect cleanup**
   - Choose Disconnect and inspect `route print`.
   - Expected: the route added by this app is gone and its UI status returns to Not applied.

6. **Close while connected**
   - Connect with the route enabled, then close the app normally.
   - Expected: the app disconnects and deletes the applied route before exiting.

7. **Recover after forced termination**
   - Connect with the route enabled, terminate SarmadiVpn from Task Manager, then relaunch it.
   - Expected: startup reads `applied-routes.json`, attempts stale route deletion, and logs the result. Confirm with `route print`.

8. **Route persistence**
   - Add a route, close and relaunch the app.
   - Expected: the route remains in the list with its label and enabled state.

9. **Delete a route**
   - Select a route and choose **Delete selected** while connected.
   - Expected: it disappears from the list and `routes.json`; Windows route deletion is attempted immediately. Confirm with `route print`.

10. **L2TP NAT traversal setting**
    - Choose **Enable L2TP behind NAT**, then inspect the PolicyAgent registry value.
    - Expected: `AssumeUDPEncapsulationContextOnSendRule` is a DWORD set to `2`; the app says a reboot is required.

11. **Input validation**
    - Try an invalid IPv4 address, invalid prefix, duplicate route, and `0.0.0.0/0`.
    - Expected: each is rejected with a useful message; a host address with a prefix is normalized to its network address.
