#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <commctrl.h>
#include <commdlg.h>
#include <ras.h>
#include <raserror.h>
#include <iphlpapi.h>
#include <iptypes.h>
#include <ipifcons.h>
#include <wincrypt.h>
#include <shlobj.h>
#include <shellapi.h>
#include <ws2tcpip.h>
#include <string>
#include <vector>
#include <array>
#include <fstream>
#include <sstream>
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cwchar>
#include <system_error>
#include <memory>
#include <objbase.h>
#include <windowsx.h>

#pragma comment(lib, "rasapi32.lib")
#pragma comment(lib, "iphlpapi.lib")
#pragma comment(lib, "crypt32.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "comdlg32.lib")
#pragma comment(lib, "ws2_32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "gdi32.lib")

namespace {
constexpr wchar_t kApp[] = L"MyVpnApp";
constexpr wchar_t kProfile[] = L"MyVpnApp";
constexpr UINT WM_RAS_STATE = WM_APP + 7;
constexpr UINT WM_APPLY_ROUTES = WM_APP + 8;
constexpr UINT WM_ROUTE_RESULT = WM_APP + 9;
constexpr UINT WM_CONNECT_START = WM_APP + 10;
constexpr int IDC_SERVER=100, IDC_USER=101, IDC_PASS=102, IDC_PSK=103, IDC_PROTOCOL=104;
constexpr int IDC_REMEMBER=105, IDC_CONNECT=106, IDC_DISCONNECT=107, IDC_NAT=108, IDC_CERT=109;
constexpr int IDC_DEST=110, IDC_LABEL=111, IDC_ADD=112, IDC_DELETE=113, IDC_ROUTES=114, IDC_LOG=115, IDC_COPY=116;
constexpr int IDC_ENABLED=117, IDC_STATUS=118;
struct Route { std::wstring id, destination, label, status; bool enabled=true; };
struct Settings { std::wstring server, username, encryptedPassword, encryptedPsk; int protocol=0; bool remember=false; };
struct Network { std::wstring address, mask; unsigned prefix=32; };
struct RouteTarget { DWORD index=0; DWORD peer=0; };
struct Applied { std::wstring id, address, mask; DWORD index=0, peer=0; };

std::wstring Utf8ToWide(const std::string& s) {
    if (s.empty()) return {};
    int n=MultiByteToWideChar(CP_UTF8,0,s.data(),(int)s.size(),nullptr,0);
    std::wstring out(n,L'\0'); MultiByteToWideChar(CP_UTF8,0,s.data(),(int)s.size(),out.data(),n); return out;
}
std::string WideToUtf8(const std::wstring& s) {
    if (s.empty()) return {};
    int n=WideCharToMultiByte(CP_UTF8,0,s.data(),(int)s.size(),nullptr,0,nullptr,nullptr);
    std::string out(n,'\0'); WideCharToMultiByte(CP_UTF8,0,s.data(),(int)s.size(),out.data(),n,nullptr,nullptr); return out;
}
std::wstring Trim(std::wstring s) {
    auto a=s.find_first_not_of(L" \t\r\n"), b=s.find_last_not_of(L" \t\r\n");
    return a==std::wstring::npos ? L"" : s.substr(a,b-a+1);
}
std::wstring JsonEscape(const std::wstring& s) {
    std::wstring o=L"\"";
    for (wchar_t c:s) { if(c==L'"'||c==L'\\'){o+=L'\\';o+=c;} else if(c==L'\n')o+=L"\\n"; else if(c==L'\r')o+=L"\\r"; else if(c==L'\t')o+=L"\\t"; else if(c>=32)o+=c; }
    return o+L'"';
}
std::wstring Field(const std::wstring& obj,const wchar_t* key) {
    std::wstring needle=L"\""+std::wstring(key)+L"\""; auto p=obj.find(needle); if(p==std::wstring::npos)return L"";
    p=obj.find(L':',p+needle.size()); if(p==std::wstring::npos)return L""; p=obj.find(L'"',p+1); if(p==std::wstring::npos)return L"";
    std::wstring out; for(++p;p<obj.size();++p){wchar_t c=obj[p]; if(c==L'"')break; if(c==L'\\'&&p+1<obj.size()){c=obj[++p]; if(c==L'n')c=L'\n'; else if(c==L'r')c=L'\r'; else if(c==L't')c=L'\t';} out+=c;} return out;
}
DWORD NumberField(const std::wstring& obj,const wchar_t* key){std::wstring n=L"\""+std::wstring(key)+L"\"";auto p=obj.find(n);if(p==obj.npos)return 0;p=obj.find(L':',p+n.size());if(p==obj.npos)return 0;return (DWORD)wcstoul(obj.c_str()+p+1,nullptr,10);}
bool BoolField(const std::wstring& obj,const wchar_t* key,bool fallback=false){std::wstring n=L"\""+std::wstring(key)+L"\"";auto p=obj.find(n);if(p==obj.npos)return fallback;p=obj.find(L':',p+n.size());if(p==obj.npos)return fallback;p=obj.find_first_not_of(L" \t",p+1);return p!=obj.npos&&obj.compare(p,4,L"true")==0;}
std::vector<std::wstring> JsonObjects(const std::wstring& json) {
    std::vector<std::wstring> out; bool quoted=false,esc=false; int depth=0; size_t start=0;
    for(size_t i=0;i<json.size();++i){wchar_t c=json[i]; if(quoted){if(esc)esc=false;else if(c==L'\\')esc=true;else if(c==L'"')quoted=false;continue;} if(c==L'"'){quoted=true;continue;} if(c==L'{'){if(depth++==0)start=i;} else if(c==L'}'&&depth>0&&--depth==0)out.push_back(json.substr(start,i-start+1));}
    return out;
}
std::wstring ReadText(const std::wstring& path) {
    std::ifstream f(path,std::ios::binary); if(!f)return L""; std::string b((std::istreambuf_iterator<char>(f)),{}); return Utf8ToWide(b);
}
bool AtomicWrite(const std::wstring& path,const std::wstring& text) {
    std::wstring tmp=path+L".tmp"; std::string b=WideToUtf8(text); HANDLE h=CreateFileW(tmp.c_str(),GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(h==INVALID_HANDLE_VALUE)return false; DWORD wrote=0; bool ok=WriteFile(h,b.data(),(DWORD)b.size(),&wrote,nullptr)&&wrote==b.size()&&FlushFileBuffers(h); CloseHandle(h);
    if(!ok){DeleteFileW(tmp.c_str());return false;} if(GetFileAttributesW(path.c_str())!=INVALID_FILE_ATTRIBUTES){if(!ReplaceFileW(path.c_str(),tmp.c_str(),nullptr,REPLACEFILE_IGNORE_MERGE_ERRORS,nullptr,nullptr)){DeleteFileW(tmp.c_str());return false;}}
    else if(!MoveFileExW(tmp.c_str(),path.c_str(),MOVEFILE_WRITE_THROUGH)){DeleteFileW(tmp.c_str());return false;} return true;
}
void Log(const std::wstring& text);
bool JsonLooksBalanced(const std::wstring& s,wchar_t root){auto first=s.find_first_not_of(L" \t\r\n"),last=s.find_last_not_of(L" \t\r\n");if(first==s.npos||s[first]!=root||s[last]!=(root==L'['?L']':L'}'))return false;std::vector<wchar_t> stack;bool quoted=false,escaped=false;for(wchar_t c:s){if(quoted){if(escaped)escaped=false;else if(c==L'\\')escaped=true;else if(c==L'"')quoted=false;continue;}if(c==L'"'){quoted=true;continue;}if(c==L'{'||c==L'[')stack.push_back(c);else if(c==L'}'||c==L']'){if(stack.empty()||(c==L'}'&&stack.back()!=L'{')||(c==L']'&&stack.back()!=L'['))return false;stack.pop_back();}}return !quoted&&stack.empty();}
bool LoadOrBackup(const std::wstring& path,std::wstring& text) {
    text=ReadText(path);if(GetFileAttributesW(path.c_str())==INVALID_FILE_ATTRIBUTES)return true;bool list=path.find(L"routes.json")!=path.npos||path.find(L"applied-routes.json")!=path.npos;
    if(JsonLooksBalanced(text,list?L'[':L'{'))return true;
    SYSTEMTIME t{};GetLocalTime(&t);wchar_t suffix[48];swprintf_s(suffix,L".corrupt-%04u%02u%02u-%02u%02u%02u",t.wYear,t.wMonth,t.wDay,t.wHour,t.wMinute,t.wSecond);if(MoveFileExW(path.c_str(),(path+suffix).c_str(),MOVEFILE_REPLACE_EXISTING))Log(L"Invalid data in "+path+L" was backed up; a new file will be created.");text=L"";return true;
}
std::wstring RandomId(){GUID g{};CoCreateGuid(&g);wchar_t s[40];StringFromGUID2(g,s,40);return s;}
std::wstring Base64(const BYTE* bytes,DWORD count){DWORD n=0;CryptBinaryToStringW(bytes,count,CRYPT_STRING_BASE64|CRYPT_STRING_NOCRLF,nullptr,&n);std::wstring s(n,L'\0');CryptBinaryToStringW(bytes,count,CRYPT_STRING_BASE64|CRYPT_STRING_NOCRLF,s.data(),&n);if(!s.empty()&&s.back()==L'\0')s.pop_back();return s;}
std::vector<BYTE> Unbase64(const std::wstring& s){DWORD n=0;if(!CryptStringToBinaryW(s.c_str(),0,CRYPT_STRING_BASE64,nullptr,&n,nullptr,nullptr))return {};std::vector<BYTE>b(n);if(!CryptStringToBinaryW(s.c_str(),0,CRYPT_STRING_BASE64,b.data(),&n,nullptr,nullptr))return {};b.resize(n);return b;}
std::wstring Protect(const std::wstring& clear){DATA_BLOB in{(DWORD)(clear.size()*sizeof(wchar_t)),(BYTE*)clear.data()},out{};if(!CryptProtectData(&in,L"MyVpnApp",nullptr,nullptr,nullptr,CRYPTPROTECT_UI_FORBIDDEN,&out))return L"";auto b=Base64(out.pbData,out.cbData);LocalFree(out.pbData);return b;}
std::wstring Unprotect(const std::wstring& encoded){auto bytes=Unbase64(encoded);if(bytes.empty())return L"";DATA_BLOB in{(DWORD)bytes.size(),bytes.data()},out{};if(!CryptUnprotectData(&in,nullptr,nullptr,nullptr,nullptr,CRYPTPROTECT_UI_FORBIDDEN,&out))return L"";std::wstring s((wchar_t*)out.pbData,out.cbData/sizeof(wchar_t));SecureZeroMemory(out.pbData,out.cbData);LocalFree(out.pbData);return s;}
bool ParseIPv4(const std::wstring& s,DWORD& out){IN_ADDR a{};if(InetPtonW(AF_INET,s.c_str(),&a)!=1)return false;out=a.s_addr;return true;}
std::wstring IpText(DWORD addr){IN_ADDR a{};a.s_addr=addr;wchar_t b[32]{};InetNtopW(AF_INET,&a,b,32);return b;}
bool ParseCidr(std::wstring value,Network& n,std::wstring& error){value=Trim(value);auto slash=value.find(L'/');if(slash!=value.npos&&value.find(L'/',slash+1)!=value.npos){error=L"Enter one IPv4 address or CIDR.";return false;}DWORD ip;if(!ParseIPv4(value.substr(0,slash),ip)){error=L"Enter a valid IPv4 address.";return false;}unsigned prefix=32;if(slash!=value.npos){auto p=value.substr(slash+1);if(p.empty()||p.size()>2||!std::all_of(p.begin(),p.end(),[](wchar_t c){return c>=L'0'&&c<=L'9';})){error=L"Prefix must be from 0 to 32.";return false;}prefix=(unsigned)_wtoi(p.c_str());if(prefix>32){error=L"Prefix must be from 0 to 32.";return false;}}
    DWORD host=ntohl(ip),mask=prefix?0xFFFFFFFFu<<(32-prefix):0; n.prefix=prefix;n.address=IpText(htonl(host&mask));n.mask=IpText(htonl(mask));return true;}
std::wstring Normalize(const Network& n){return n.address+L"/"+std::to_wstring(n.prefix);}
std::wstring AppDir(){wchar_t p[MAX_PATH];if(FAILED(SHGetFolderPathW(nullptr,CSIDL_APPDATA|CSIDL_FLAG_CREATE,nullptr,SHGFP_TYPE_CURRENT,p)))return L"";std::wstring d=p;d+=L"\\MyVpnApp";CreateDirectoryW(d.c_str(),nullptr);return d;}
std::wstring FriendlyError(DWORD code){wchar_t msg[512]{};if(RasGetErrorStringW(code,msg,512)==0&&msg[0])return std::wstring(L"RAS error ")+std::to_wstring(code)+L": "+msg;return L"RAS error "+std::to_wstring(code)+L". Check the VPN address, credentials, protocol, and server.";}
}

namespace {
struct AppState {
    HWND window{}, server{}, user{}, password{}, psk{}, protocol{}, remember{}, connect{}, disconnect{}, nat{}, cert{};
    HWND destination{}, label{}, add{}, del{}, list{}, log{}, copy{}, enabled{}, status{};
    HFONT font{}; std::wstring dir, routesPath, settingsPath, appliedPath; Settings settings; std::vector<Route> routes; std::vector<Applied> applied;
    HRASCONN connection{}; bool connected=false, closing=false, routeMutation=false, disconnectRequested=false;
} g;
struct RouteWorkerResult { std::vector<std::pair<std::wstring,std::wstring>> statuses; std::vector<Applied> applied; std::vector<std::wstring> logs; };
void CALLBACK RasCallback(HRASCONN h, UINT, RASCONNSTATE state, DWORD error, DWORD) { if(h)g.connection=h; if(g.window)PostMessageW(g.window,WM_RAS_STATE,(WPARAM)state,(LPARAM)error); }

void Log(const std::wstring& text){if(!g.log)return;SYSTEMTIME t{};GetLocalTime(&t);wchar_t line[32];swprintf_s(line,L"%02u:%02u:%02u  ",t.wHour,t.wMinute,t.wSecond);int end=GetWindowTextLengthW(g.log);SendMessageW(g.log,EM_SETSEL,end,end);std::wstring full=line+text+L"\r\n";SendMessageW(g.log,EM_REPLACESEL,FALSE,(LPARAM)full.c_str());SendMessageW(g.log,EM_SCROLLCARET,0,0);}
std::wstring Text(HWND h){int n=GetWindowTextLengthW(h);std::wstring s(n+1,L'\0');if(n)GetWindowTextW(h,s.data(),n+1);s.resize(n);return s;}
void PersistSettings(){g.settings.server=Text(g.server);g.settings.username=Text(g.user);g.settings.protocol=(int)SendMessageW(g.protocol,CB_GETCURSEL,0,0);g.settings.remember=SendMessageW(g.remember,BM_GETCHECK,0,0)==BST_CHECKED;
    std::wstring password=Text(g.password),psk=Text(g.psk);g.settings.encryptedPassword=g.settings.remember?Protect(password):L"";g.settings.encryptedPsk=Protect(psk);
    std::wstring json=L"{\n\"server\":"+JsonEscape(g.settings.server)+L",\n\"username\":"+JsonEscape(g.settings.username)+L",\n\"protocol\":"+JsonEscape(g.settings.protocol?L"L2tp":L"Sstp")+L",\n\"remember\":"+(g.settings.remember?L"true":L"false")+L",\n\"password\":"+JsonEscape(g.settings.encryptedPassword)+L",\n\"psk\":"+JsonEscape(g.settings.encryptedPsk)+L"}\n";
    if(!AtomicWrite(g.settingsPath,json))Log(L"Could not atomically save settings. Check AppData permissions and disk space.");SecureZeroMemory(password.data(),password.size()*sizeof(wchar_t));SecureZeroMemory(psk.data(),psk.size()*sizeof(wchar_t));}
bool SaveRoutes(){std::wstring j=L"[\n";for(size_t i=0;i<g.routes.size();++i){auto&r=g.routes[i];j+=L"{\"id\":"+JsonEscape(r.id)+L",\"destination\":"+JsonEscape(r.destination)+L",\"label\":"+JsonEscape(r.label)+L",\"enabled\":"+(r.enabled?L"true":L"false")+L"}"+(i+1<g.routes.size()?L",":L"")+L"\n";}j+=L"]\n";return AtomicWrite(g.routesPath,j);}
bool SaveAppliedList(const std::vector<Applied>& values){std::wstring j=L"[\n";for(size_t i=0;i<values.size();++i){auto&a=values[i];j+=L"{\"id\":"+JsonEscape(a.id)+L",\"address\":"+JsonEscape(a.address)+L",\"mask\":"+JsonEscape(a.mask)+L",\"index\":"+std::to_wstring(a.index)+L",\"peer\":"+std::to_wstring(a.peer)+L"}"+(i+1<values.size()?L",":L"")+L"\n";}return AtomicWrite(g.appliedPath,j+L"]\n");}
bool SaveApplied(){return SaveAppliedList(g.applied);}
void RenderRoutes(){ListView_DeleteAllItems(g.list);for(size_t i=0;i<g.routes.size();++i){auto&r=g.routes[i];LVITEMW item{};item.mask=LVIF_TEXT|LVIF_PARAM;item.iItem=(int)i;item.lParam=(LPARAM)i;std::wstring enabled=r.enabled?L"Yes":L"No";item.pszText=enabled.data();ListView_InsertItem(g.list,&item);ListView_SetCheckState(g.list,(int)i,r.enabled);ListView_SetItemText(g.list,(int)i,1,r.destination.data());ListView_SetItemText(g.list,(int)i,2,r.label.data());ListView_SetItemText(g.list,(int)i,3,r.status.data());}}
bool LoadRoutes(){std::wstring j;LoadOrBackup(g.routesPath,j);if(j.empty())return true;auto objs=JsonObjects(j);if(j.find(L'[')!=j.npos&&objs.empty()&&j.find(L']')==j.npos){SYSTEMTIME t{};GetLocalTime(&t);wchar_t suffix[48];swprintf_s(suffix,L".corrupt-%04u%02u%02u-%02u%02u%02u",t.wYear,t.wMonth,t.wDay,t.wHour,t.wMinute,t.wSecond);MoveFileExW(g.routesPath.c_str(),(g.routesPath+suffix).c_str(),MOVEFILE_REPLACE_EXISTING);Log(L"The route file was corrupt; it was backed up and a new list was started.");return true;}
    for(auto&o:objs){Route r;r.id=Field(o,L"id");r.destination=Field(o,L"destination");r.label=Field(o,L"label");r.enabled=BoolField(o,L"enabled",true);r.status=L"Not applied";Network n;std::wstring e;if(ParseCidr(r.destination,n,e)&&n.prefix>0){r.destination=Normalize(n);if(std::none_of(g.routes.begin(),g.routes.end(),[&](auto&x){return x.destination==r.destination;}))g.routes.push_back(r);}}
    return true;}
void LoadSettings(){std::wstring j;LoadOrBackup(g.settingsPath,j);g.settings.server=Field(j,L"server");g.settings.username=Field(j,L"username");g.settings.protocol=Field(j,L"protocol")==L"L2tp";g.settings.remember=BoolField(j,L"remember");g.settings.encryptedPassword=Field(j,L"password");g.settings.encryptedPsk=Field(j,L"psk");}
void LoadApplied(){std::wstring j;LoadOrBackup(g.appliedPath,j);for(auto&o:JsonObjects(j)){Applied a{Field(o,L"id"),Field(o,L"address"),Field(o,L"mask"),NumberField(o,L"index"),NumberField(o,L"peer")};if(!a.address.empty()&&!a.mask.empty())g.applied.push_back(a);}}

bool AddRouteNative(const Network& n,const RouteTarget& target,DWORD& error){MIB_IPFORWARDROW row{};ParseIPv4(n.address,row.dwForwardDest);ParseIPv4(n.mask,row.dwForwardMask);row.dwForwardNextHop=target.peer;row.dwForwardIfIndex=target.index;row.dwForwardType=target.peer?MIB_IPROUTE_TYPE_INDIRECT:MIB_IPROUTE_TYPE_DIRECT;row.dwForwardProto=MIB_IPPROTO_NETMGMT;row.dwForwardMetric1=1;error=CreateIpForwardEntry(&row);return error==NO_ERROR;}
bool DeleteRouteNative(const Applied& a,DWORD& error){MIB_IPFORWARDROW row{};ParseIPv4(a.address,row.dwForwardDest);ParseIPv4(a.mask,row.dwForwardMask);row.dwForwardNextHop=a.peer;row.dwForwardIfIndex=a.index;error=DeleteIpForwardEntry(&row);return error==NO_ERROR||error==ERROR_NOT_FOUND;}
bool ResolveTarget(RouteTarget& target,std::wstring& source){
    if(!g.connection)return false;DWORD cb=sizeof(RASPPPIP);RASPPPIP ppp{};ppp.dwSize=sizeof(ppp);DWORD e=RasGetProjectionInfoW(g.connection,RASP_PppIp,&ppp,&cb);
    if(e!=ERROR_SUCCESS||!ppp.szIpAddress[0]){source=L"RAS projection did not return a local IPv4 address";return false;}
    DWORD local=0;ParseIPv4(ppp.szIpAddress,local);target.peer=0;if(ppp.szServerIpAddress[0])ParseIPv4(ppp.szServerIpAddress,target.peer);
    ULONG size=0;GetAdaptersInfo(nullptr,&size);if(!size){source=L"GetAdaptersInfo could not determine the adapter buffer size";return false;}std::vector<BYTE> buf(size);auto* adapters=(IP_ADAPTER_INFO*)buf.data();ULONG r=GetAdaptersInfo(adapters,&size);
    if(r!=NO_ERROR){source=L"GetAdaptersInfo failed: "+std::to_wstring(r);return false;}
    for(auto*a=adapters;a;a=a->Next)for(auto*u=&a->IpAddressList;u;u=u->Next){IN_ADDR addr{};if(InetPtonA(AF_INET,u->IpAddress.String,&addr)==1&&addr.s_addr==local){target.index=a->Index;source=L"RAS PPP peer "+std::wstring(ppp.szServerIpAddress[0]?ppp.szServerIpAddress:L"(on-link)")+L", adapter index "+std::to_wstring(target.index);return target.index>0;}}
    source=L"The PPP local address was not matched to an adapter";return false;
}
DWORD WINAPI RouteWorker(void*){auto result=new RouteWorkerResult;result->applied=g.applied;RouteTarget target{};std::wstring source;if(!ResolveTarget(target,source)){result->logs.push_back(L"Route gateway discovery failed: "+source);for(auto&r:g.routes)if(r.enabled)result->statuses.push_back({r.id,L"Failed: "+source});}
    else{result->logs.push_back(L"Discovered VPN route target: "+source);result->applied=g.applied;for(auto&r:g.routes){if(!r.enabled)continue;if(std::any_of(result->applied.begin(),result->applied.end(),[&](auto&a){return a.id==r.id;})){result->statuses.push_back({r.id,L"Applied"});continue;}Network n;std::wstring error;if(!ParseCidr(r.destination,n,error)){result->statuses.push_back({r.id,L"Failed: "+error});continue;}DWORD ec=ERROR_GEN_FAILURE;bool done=false;for(int tries=0;tries<3&&!done;++tries){done=AddRouteNative(n,target,ec);if(!done)Sleep(500);}if(done){result->applied.push_back({r.id,n.address,n.mask,target.index,target.peer});if(!SaveAppliedList(result->applied))result->logs.push_back(L"Warning: route was installed, but cleanup tracking could not be saved.");result->statuses.push_back({r.id,L"Applied"});result->logs.push_back(L"Applied "+Normalize(n)+L" using interface "+std::to_wstring(target.index)+L".");}else{result->statuses.push_back({r.id,L"Failed: Windows error "+std::to_wstring(ec)});result->logs.push_back(L"Add route failed (Windows error "+std::to_wstring(ec)+L").");}}}
    if(!PostMessageW(g.window,WM_ROUTE_RESULT,0,(LPARAM)result))delete result;return 0;}
void ApplyRoutes(){if(!g.connected||g.routeMutation)return;g.routeMutation=true;EnableWindow(g.list,FALSE);auto h=CreateThread(nullptr,0,RouteWorker,nullptr,0,nullptr);if(h)CloseHandle(h);else{g.routeMutation=false;EnableWindow(g.list,TRUE);Log(L"Could not start the route worker thread.");}}
void CleanupRoutes(){std::vector<std::pair<std::wstring,DWORD>> failed;for(auto it=g.applied.begin();it!=g.applied.end();){DWORD ec=0;if(DeleteRouteNative(*it,ec)){Log(L"Removed route "+it->address+L".");it=g.applied.erase(it);SaveApplied();}else{Log(L"Could not remove route "+it->address+L" (Windows error "+std::to_wstring(ec)+L").");failed.push_back({it->id,ec});++it;}}for(auto&r:g.routes){auto f=std::find_if(failed.begin(),failed.end(),[&](auto&x){return x.first==r.id;});r.status=f==failed.end()?L"Not applied":L"Failed to remove (Windows error "+std::to_wstring(f->second)+L")";}RenderRoutes();}
void CleanupStale(){std::vector<Applied> failed;for(auto&a:g.applied){DWORD ec=0;if(DeleteRouteNative(a,ec))Log(L"Removed stale route "+a.address+L" from the prior run.");else{Log(L"Stale route cleanup failed for "+a.address+L" (Windows error "+std::to_wstring(ec)+L").");failed.push_back(a);}}g.applied=std::move(failed);SaveApplied();}
DWORD ConfigureProfile(const std::wstring& server,int protocol,const std::wstring& psk){if(server.size()>RAS_MaxPhoneNumber)return ERROR_INVALID_PARAMETER;RASENTRYW entry{};DWORD size=sizeof(entry),deviceSize=0;entry.dwSize=sizeof(entry);DWORD e=RasGetEntryPropertiesW(nullptr,kProfile,&entry,&size,nullptr,&deviceSize);if(e!=ERROR_SUCCESS){ZeroMemory(&entry,sizeof(entry));entry.dwSize=sizeof(entry);}
    entry.dwfOptions|=RASEO_RequireMsCHAP2|RASEO_RequireDataEncryption;entry.dwfOptions&=~RASEO_RemoteDefaultGateway;entry.dwfNetProtocols=RASNP_Ip;entry.dwFramingProtocol=RASFP_Ppp;entry.dwType=RASET_Vpn;entry.dwEncryptionType=ET_Require;entry.dwVpnStrategy=protocol?VS_L2tpOnly:VS_SstpOnly;wcscpy_s(entry.szDeviceType,RASDT_Vpn);wcscpy_s(entry.szDeviceName,protocol?L"WAN Miniport (L2TP)":L"WAN Miniport (SSTP)");wcscpy_s(entry.szLocalPhoneNumber,server.c_str());
    if(protocol)entry.dwfOptions2|=RASEO2_UsePreSharedKey;else entry.dwfOptions2&=~RASEO2_UsePreSharedKey;
    e=RasSetEntryPropertiesW(nullptr,kProfile,&entry,sizeof(entry),nullptr,0);if(e!=ERROR_SUCCESS)return e;
    if(protocol){if(psk.size()>PWLEN)return ERROR_INVALID_PARAMETER;RASCREDENTIALSW cred{};cred.dwSize=sizeof(cred);cred.dwMask=RASCM_PreSharedKey;wcscpy_s(cred.szPassword,psk.c_str());e=RasSetCredentialsW(nullptr,kProfile,&cred,FALSE);SecureZeroMemory(&cred,sizeof(cred));if(e!=ERROR_SUCCESS)return e;}return ERROR_SUCCESS;}
DWORD StartDial(const std::wstring& username,const std::wstring& password){if(username.size()>UNLEN||password.size()>PWLEN)return ERROR_INVALID_PARAMETER;RASDIALPARAMSW params{};params.dwSize=sizeof(params);wcscpy_s(params.szEntryName,kProfile);wcscpy_s(params.szUserName,username.c_str());wcscpy_s(params.szPassword,password.c_str());params.szDomain[0]=0;g.connection=nullptr;DWORD e=RasDialW(nullptr,nullptr,&params,1,(RASDIALFUNC1)RasCallback,&g.connection);SecureZeroMemory(&params,sizeof(params));return e;}
bool IsConnected(){if(!g.connection)return false;RASCONNSTATUSW s{};s.dwSize=sizeof(s);DWORD e=RasGetConnectStatusW(g.connection,&s);return e==ERROR_SUCCESS&&s.rasconnstate==RASCS_Connected;}
void SetStatus(const wchar_t* value);
void Disconnect(){if(!g.connection){g.connected=false;SetStatus(L"Disconnected");CleanupRoutes();Log(L"Disconnected.");return;}g.disconnectRequested=true;DWORD e=RasHangUpW(g.connection);if(e!=ERROR_SUCCESS&&e!=ERROR_INVALID_HANDLE){g.disconnectRequested=false;SetStatus(L"Error");Log(L"RasHangUp failed (RAS error "+std::to_wstring(e)+L"): "+FriendlyError(e));return;}SetStatus(L"Disconnecting");Log(L"Disconnect requested; waiting for RAS to finish.");SetTimer(g.window,2,500,nullptr);}
void SetProtocolVisibility(){bool l2tp=SendMessageW(g.protocol,CB_GETCURSEL,0,0)==1;ShowWindow(g.psk,l2tp?SW_SHOW:SW_HIDE);}
void SetStatus(const wchar_t* value){if(g.status)SetWindowTextW(g.status,value);}
void SetButtons(){bool busy=g.routeMutation;EnableWindow(g.connect,!g.connected&&!busy);EnableWindow(g.disconnect,!busy&&(g.connected||g.connection!=nullptr));EnableWindow(g.add,!busy);EnableWindow(g.del,!busy);}
void OnDialStart(){std::wstring server=Trim(Text(g.server)),username=Trim(Text(g.user)),password=Text(g.password),psk=Text(g.psk);int protocol=(int)SendMessageW(g.protocol,CB_GETCURSEL,0,0);if(server.empty()||username.empty()||password.empty()||(protocol==1&&psk.empty())){MessageBoxW(g.window,L"Enter the server, username, password, and L2TP pre-shared key when using L2TP.",kApp,MB_ICONWARNING);return;}
    PersistSettings();DWORD e=ConfigureProfile(server,protocol,psk);if(e!=ERROR_SUCCESS){Log(L"VPN profile setup failed: "+FriendlyError(e));MessageBoxW(g.window,(L"Could not configure the Windows VPN profile. "+FriendlyError(e)).c_str(),L"VPN setup failed",MB_ICONERROR);return;}
    Log(L"Connecting to "+server+L" using "+(protocol?L"L2TP/IPsec":L"SSTP")+L" through Windows RAS.");SetStatus(L"Connecting");SetWindowTextW(g.connect,L"Connecting...");EnableWindow(g.connect,FALSE);EnableWindow(g.disconnect,TRUE);DWORD dial=StartDial(username,password);SecureZeroMemory(password.data(),password.size()*sizeof(wchar_t));SecureZeroMemory(psk.data(),psk.size()*sizeof(wchar_t));
    if(dial!=ERROR_SUCCESS){SetStatus(L"Error");SetWindowTextW(g.connect,L"Connect");EnableWindow(g.disconnect,FALSE);Log(FriendlyError(dial));MessageBoxW(g.window,FriendlyError(dial).c_str(),L"VPN connection failed",MB_ICONERROR);g.connection=nullptr;return;}SetTimer(g.window,1,1000,nullptr);}
void EnableNat(){HKEY key{};LONG e=RegCreateKeyExW(HKEY_LOCAL_MACHINE,L"SYSTEM\\CurrentControlSet\\Services\\PolicyAgent",0,nullptr,0,KEY_SET_VALUE,nullptr,&key,nullptr);if(e==ERROR_SUCCESS){DWORD value=2;e=RegSetValueExW(key,L"AssumeUDPEncapsulationContextOnSendRule",0,REG_DWORD,(BYTE*)&value,sizeof(value));RegCloseKey(key);}if(e!=ERROR_SUCCESS){wchar_t msg[256]{};FormatMessageW(FORMAT_MESSAGE_FROM_SYSTEM|FORMAT_MESSAGE_IGNORE_INSERTS,nullptr,e,0,msg,256,nullptr);MessageBoxW(g.window,(L"Could not update the PolicyAgent registry value. Run as administrator. "+std::wstring(msg)).c_str(),L"Registry error",MB_ICONERROR);}else MessageBoxW(g.window,L"L2TP NAT traversal is enabled. Restart Windows before connecting.",L"Restart required",MB_ICONINFORMATION);}
void ShowCertificateHelp(){MessageBoxW(g.window,L"SSTP certificate help:\n\n0x800B0109: the certificate chain is not trusted. Ask the server administrator to install a certificate from a trusted CA.\n\n0x800B010F: the server name does not match the certificate. Connect using the certificate's DNS hostname, not an IP address.\n\n0x80092013: certificate revocation status could not be checked. Check internet access, proxy settings, and the CA revocation endpoints.\n\nWindows 7 needs current servicing updates and TLS 1.2 enabled for modern SSTP servers. No CA certificate is bundled with this app, so it will not import an unverified root certificate.",L"SSTP certificate help",MB_ICONINFORMATION);}
void AddRouteFromUi(){Network n;std::wstring error;if(!ParseCidr(Text(g.destination),n,error)){MessageBoxW(g.window,error.c_str(),L"Invalid route",MB_ICONWARNING);return;}if(n.prefix==0){MessageBoxW(g.window,L"0.0.0.0/0 would send all traffic through the VPN. Choose a narrower route.",L"Default route blocked",MB_ICONWARNING);return;}std::wstring cidr=Normalize(n);if(std::any_of(g.routes.begin(),g.routes.end(),[&](auto&r){return r.destination==cidr;})){MessageBoxW(g.window,L"That route already exists.",L"Duplicate route",MB_ICONWARNING);return;}Route r{RandomId(),cidr,Trim(Text(g.label)),L"Not applied",true};g.routes.push_back(r);if(!SaveRoutes()){g.routes.pop_back();MessageBoxW(g.window,L"Could not save routes.json. Check disk space and AppData permissions.",L"Route not added",MB_ICONERROR);return;}RenderRoutes();SetWindowTextW(g.destination,L"");SetWindowTextW(g.label,L"");if(g.connected)ApplyRoutes();}
void DeleteSelected(){int i=ListView_GetNextItem(g.list,-1,LVNI_SELECTED);if(i<0||i>=(int)g.routes.size())return;auto r=g.routes[i];auto applied=std::find_if(g.applied.begin(),g.applied.end(),[&](auto&a){return a.id==r.id;});if(applied!=g.applied.end()){DWORD e=0;if(DeleteRouteNative(*applied,e)){Log(L"Removed route "+applied->address);g.applied.erase(applied);SaveApplied();}else{Log(L"Route delete failed (Windows error "+std::to_wstring(e)+L"). It remains in the cleanup record.");MessageBoxW(g.window,L"Windows could not remove the route. It was removed from the saved list; the cleanup record is kept for the next launch.",L"Route cleanup incomplete",MB_ICONWARNING);}}
    g.routes.erase(g.routes.begin()+i);if(!SaveRoutes())MessageBoxW(g.window,L"Could not save the deleted route list.",L"Save failed",MB_ICONERROR);RenderRoutes();}

HWND Control(const wchar_t* klass,const wchar_t* text,DWORD style,int x,int y,int w,int h,int id=0,DWORD ex=0){return CreateWindowExW(ex,klass,text,WS_CHILD|WS_VISIBLE|style,x,y,w,h,g.window,(HMENU)(INT_PTR)id,GetModuleHandleW(nullptr),nullptr);}
void Label(const wchar_t* text,int x,int y,int w){Control(L"STATIC",text,0,x,y,w,22);}
void Layout(HWND hwnd){RECT r{};GetClientRect(hwnd,&r);int width=r.right-40;SetWindowPos(g.log,nullptr,20,540,width,150,SWP_NOZORDER);SetWindowPos(g.list,nullptr,20,390,width,130,SWP_NOZORDER);}
void CreateUi(HWND hwnd){g.window=hwnd;g.font=(HFONT)GetStockObject(DEFAULT_GUI_FONT);
    Label(L"MyVpnApp  |  Windows VPN and split-tunnel routes",20,14,700);g.status=Control(L"STATIC",L"Disconnected",SS_LEFT,800,14,220,24,IDC_STATUS);
    Label(L"Server address",20,50,130);g.server=Control(L"EDIT",L"",WS_BORDER|ES_AUTOHSCROLL,20,72,255,25,IDC_SERVER);
    Label(L"Username",290,50,100);g.user=Control(L"EDIT",L"",WS_BORDER|ES_AUTOHSCROLL,290,72,190,25,IDC_USER);
    Label(L"Password",490,50,100);g.password=Control(L"EDIT",L"",WS_BORDER|ES_PASSWORD|ES_AUTOHSCROLL,490,72,190,25,IDC_PASS);
    Label(L"Protocol",690,50,100);g.protocol=Control(L"COMBOBOX",L"",CBS_DROPDOWNLIST|WS_VSCROLL,690,72,160,180,IDC_PROTOCOL);SendMessageW(g.protocol,CB_ADDSTRING,0,(LPARAM)L"SSTP");SendMessageW(g.protocol,CB_ADDSTRING,0,(LPARAM)L"L2TP/IPsec");SendMessageW(g.protocol,CB_SETCURSEL,0,0);
    Label(L"L2TP pre-shared key",20,108,180);g.psk=Control(L"EDIT",L"",WS_BORDER|ES_PASSWORD|ES_AUTOHSCROLL,20,130,255,25,IDC_PSK);
    g.remember=Control(L"BUTTON",L"Remember credentials",BS_AUTOCHECKBOX,290,130,170,25,IDC_REMEMBER);
    g.connect=Control(L"BUTTON",L"Connect",BS_PUSHBUTTON,490,130,90,30,IDC_CONNECT);g.disconnect=Control(L"BUTTON",L"Disconnect",BS_PUSHBUTTON,590,130,90,30,IDC_DISCONNECT);g.nat=Control(L"BUTTON",L"Enable L2TP behind NAT",BS_PUSHBUTTON,690,130,160,30,IDC_NAT);g.cert=Control(L"BUTTON",L"SSTP certificate help",BS_PUSHBUTTON,860,130,160,30,IDC_CERT);
    Label(L"Split-tunnel routes — only enabled destinations use the VPN",20,180,600);
    Label(L"IPv4 address or CIDR",20,210,180);g.destination=Control(L"EDIT",L"",WS_BORDER|ES_AUTOHSCROLL,20,232,210,25,IDC_DEST);
    Label(L"Label (optional)",240,210,140);g.label=Control(L"EDIT",L"",WS_BORDER|ES_AUTOHSCROLL,240,232,220,25,IDC_LABEL);g.add=Control(L"BUTTON",L"Add route",BS_PUSHBUTTON,470,230,90,30,IDC_ADD);g.del=Control(L"BUTTON",L"Delete selected",BS_PUSHBUTTON,570,230,120,30,IDC_DELETE);g.copy=Control(L"BUTTON",L"Copy log",BS_PUSHBUTTON,700,230,90,30,IDC_COPY);
    g.list=Control(WC_LISTVIEWW,L"",LVS_REPORT|LVS_SINGLESEL|LVS_SHOWSELALWAYS,20,275,1000,245,IDC_ROUTES,WS_EX_CLIENTEDGE);ListView_SetExtendedListViewStyle(g.list,LVS_EX_FULLROWSELECT|LVS_EX_GRIDLINES|LVS_EX_CHECKBOXES);
    const wchar_t* cols[]={L"Enabled",L"Destination",L"Label",L"Status"};int widths[]={80,220,220,300};for(int i=0;i<4;++i){LVCOLUMNW c{};c.mask=LVCF_TEXT|LVCF_WIDTH;c.pszText=(LPWSTR)cols[i];c.cx=widths[i];ListView_InsertColumn(g.list,i,&c);}
    Label(L"Activity log",20,520,120);g.log=Control(L"EDIT",L"",WS_BORDER|ES_MULTILINE|ES_AUTOVSCROLL|ES_READONLY|WS_VSCROLL,20,540,1000,150,IDC_LOG,WS_EX_CLIENTEDGE);
    HWND controls[]={g.server,g.user,g.password,g.psk,g.protocol,g.remember,g.connect,g.disconnect,g.nat,g.cert,g.destination,g.label,g.add,g.del,g.list,g.log,g.copy,g.status};for(HWND c:controls)SendMessageW(c,WM_SETFONT,(WPARAM)g.font,TRUE);
    g.dir=AppDir();if(g.dir.empty()){MessageBoxW(hwnd,L"Could not locate the current user's AppData folder.",kApp,MB_ICONERROR);return;}g.routesPath=g.dir+L"\\routes.json";g.settingsPath=g.dir+L"\\settings.json";g.appliedPath=g.dir+L"\\applied-routes.json";
    LoadSettings();LoadRoutes();LoadApplied();SetWindowTextW(g.server,g.settings.server.c_str());SetWindowTextW(g.user,g.settings.username.c_str());SendMessageW(g.protocol,CB_SETCURSEL,g.settings.protocol,0);SendMessageW(g.remember,BM_SETCHECK,g.settings.remember?BST_CHECKED:BST_UNCHECKED,0);if(g.settings.remember){auto p=Unprotect(g.settings.encryptedPassword);SetWindowTextW(g.password,p.c_str());SecureZeroMemory(p.data(),p.size()*sizeof(wchar_t));}auto psk=Unprotect(g.settings.encryptedPsk);SetWindowTextW(g.psk,psk.c_str());SecureZeroMemory(psk.data(),psk.size()*sizeof(wchar_t));SetProtocolVisibility();RenderRoutes();CleanupStale();SetButtons();Log(L"Ready. Settings and routes are stored under %APPDATA%\\MyVpnApp.");
}
LRESULT CALLBACK WindowProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp){switch(msg){
case WM_CREATE:CreateUi(hwnd);return 0;
case WM_SIZE:Layout(hwnd);return 0;
case WM_COMMAND:switch(LOWORD(wp)){case IDC_CONNECT:OnDialStart();break;case IDC_DISCONNECT:Disconnect();break;case IDC_PROTOCOL:SetProtocolVisibility();break;case IDC_NAT:EnableNat();break;case IDC_CERT:ShowCertificateHelp();break;case IDC_ADD:AddRouteFromUi();break;case IDC_DELETE:DeleteSelected();break;case IDC_COPY:{int n=GetWindowTextLengthW(g.log);HGLOBAL mem=GlobalAlloc(GMEM_MOVEABLE,(n+1)*sizeof(wchar_t));if(mem&&OpenClipboard(hwnd)){EmptyClipboard();auto p=(wchar_t*)GlobalLock(mem);GetWindowTextW(g.log,p,n+1);GlobalUnlock(mem);SetClipboardData(CF_UNICODETEXT,mem);CloseClipboard();}else if(mem)GlobalFree(mem);break;}}return 0;
case WM_NOTIFY:if(((LPNMHDR)lp)->idFrom==IDC_ROUTES&&((LPNMHDR)lp)->code==LVN_ITEMCHANGED){auto*n=(NMLISTVIEW*)lp;if((n->uChanged&LVIF_STATE)&&n->iItem>=0&&n->iItem<(int)g.routes.size()){bool checked=ListView_GetCheckState(g.list,n->iItem)!=FALSE;auto&r=g.routes[n->iItem];if(checked!=r.enabled){r.enabled=checked;if(!SaveRoutes())Log(L"Could not save route enable state.");if(!checked){auto a=std::find_if(g.applied.begin(),g.applied.end(),[&](auto&x){return x.id==r.id;});if(a!=g.applied.end()){DWORD e=0;if(DeleteRouteNative(*a,e)){g.applied.erase(a);SaveApplied();r.status=L"Not applied";}else r.status=L"Failed to remove (Windows error "+std::to_wstring(e)+L")";}}else if(g.connected)ApplyRoutes();ListView_SetItemText(g.list,n->iItem,0,r.enabled?(LPWSTR)L"Yes":(LPWSTR)L"No");ListView_SetItemText(g.list,n->iItem,3,r.status.data());}}}return 0;
case WM_RAS_STATE:{auto state=(RASCONNSTATE)wp;DWORD error=(DWORD)lp;if(state==RASCS_Connected){g.connected=true;g.disconnectRequested=false;SetStatus(L"Connected");SetWindowTextW(g.connect,L"Connected");Log(L"VPN connected.");SetTimer(hwnd,1,1000,nullptr);ApplyRoutes();SetButtons();}else if(state==RASCS_Disconnected){bool expected=g.disconnectRequested;g.disconnectRequested=false;g.connected=false;g.connection=nullptr;KillTimer(hwnd,1);SetStatus(error&&!expected?L"Error":L"Disconnected");SetWindowTextW(g.connect,L"Connect");if(error)Log(L"VPN disconnected: "+FriendlyError(error));else Log(L"VPN disconnected.");if(error&&!expected)MessageBoxW(hwnd,FriendlyError(error).c_str(),L"VPN connection failed",MB_ICONERROR);CleanupRoutes();if(g.closing)DestroyWindow(hwnd);else SetButtons();}else if(state==RASCS_Authenticate)Log(L"Authenticating with the VPN server...");else if(error){SetStatus(L"Error");Log(L"RAS connection error: "+FriendlyError(error));}return 0;}
case WM_ROUTE_RESULT:{std::unique_ptr<RouteWorkerResult> r((RouteWorkerResult*)lp);g.routeMutation=false;EnableWindow(g.list,TRUE);if(r){g.applied=std::move(r->applied);SaveApplied();for(auto&x:r->statuses)for(auto&route:g.routes)if(route.id==x.first)route.status=x.second;for(auto&s:r->logs)Log(s);if(!g.connected)CleanupRoutes();else RenderRoutes();}SetButtons();if(g.closing){if(g.connection){Disconnect();CleanupRoutes();SetTimer(hwnd,2,1200,nullptr);}else DestroyWindow(hwnd);}return 0;}
case WM_TIMER:if(wp==1){if(g.connection){RASCONNSTATUSW s{};s.dwSize=sizeof(s);DWORD e=RasGetConnectStatusW(g.connection,&s);if(e==ERROR_INVALID_HANDLE||s.rasconnstate==RASCS_Disconnected){g.connected=false;g.connection=nullptr;KillTimer(hwnd,1);SetStatus(L"Disconnected");SetWindowTextW(g.connect,L"Connect");CleanupRoutes();SetButtons();if(g.closing)DestroyWindow(hwnd);}else if(s.rasconnstate==RASCS_Connected&&!g.connected){g.connected=true;SetStatus(L"Connected");SetWindowTextW(g.connect,L"Connected");Log(L"VPN connected (state poll).");ApplyRoutes();SetButtons();}}}else if(wp==2){KillTimer(hwnd,2);if(g.closing){if(g.routeMutation)SetTimer(hwnd,2,500,nullptr);else if(g.connection){Disconnect();CleanupRoutes();SetTimer(hwnd,2,1200,nullptr);}else DestroyWindow(hwnd);}}return 0;
case WM_CLOSE:if(g.closing)return 0;g.closing=true;PersistSettings();if(g.routeMutation)return 0;if(g.connection){Disconnect();CleanupRoutes();SetTimer(hwnd,2,1200,nullptr);}else DestroyWindow(hwnd);return 0;
case WM_DESTROY:if(!g.applied.empty())CleanupRoutes();PostQuitMessage(0);return 0;}
return DefWindowProcW(hwnd,msg,wp,lp);}
}

#ifndef MYVPN_TESTS
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,LPWSTR,int show){INITCOMMONCONTROLSEX ic{sizeof(ic),ICC_LISTVIEW_CLASSES};InitCommonControlsEx(&ic);SetProcessDPIAware();WNDCLASSEXW wc{sizeof(wc)};wc.lpfnWndProc=WindowProc;wc.hInstance=instance;wc.hCursor=LoadCursor(nullptr,IDC_ARROW);wc.hIcon=LoadIcon(nullptr,IDI_APPLICATION);wc.hbrBackground=(HBRUSH)(COLOR_WINDOW+1);wc.lpszClassName=L"MyVpnAppWindow";RegisterClassExW(&wc);HWND hwnd=CreateWindowExW(0,wc.lpszClassName,kApp,WS_OVERLAPPEDWINDOW|WS_VISIBLE,CW_USEDEFAULT,CW_USEDEFAULT,1080,760,nullptr,nullptr,instance,nullptr);if(!hwnd)return 1;ShowWindow(hwnd,show);UpdateWindow(hwnd);MSG m;while(GetMessageW(&m,nullptr,0,0)>0){TranslateMessage(&m);DispatchMessageW(&m);}return (int)m.wParam;}
#endif
