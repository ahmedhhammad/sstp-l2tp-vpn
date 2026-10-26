#define MYVPN_TESTS
#include "MyVpnApp.cpp"
#include <iostream>

int main() {
    int failures=0;
    auto check=[&](bool ok,const wchar_t* name){std::wcout<<(ok?L"PASS ":L"FAIL ")<<name<<L"\n";if(!ok)++failures;};
    const std::pair<const wchar_t*,const wchar_t*> cases[]={
        {L"10.2.3.4/0",L"0.0.0.0/0"},{L"10.2.3.4/8",L"10.0.0.0/8"},
        {L"10.2.3.4/16",L"10.2.0.0/16"},{L"192.168.1.77/24",L"192.168.1.0/24"},
        {L"192.168.1.5/30",L"192.168.1.4/30"},{L"192.168.1.5/32",L"192.168.1.5/32"}};
    for(auto&c:cases){Network n{};std::wstring error;bool ok=ParseCidr(c.first,n,error)&&Normalize(n)==c.second;check(ok,c.first);}
    for(auto invalid:{L"abc",L"256.1.1.1",L"1.2.3.4/33",L"1.2.3.4/-1",L"1.2.3.4/1/2"}){Network n{};std::wstring error;check(!ParseCidr(invalid,n,error),invalid);}
    Network n{};std::wstring error;ParseCidr(L"192.0.2.1/24",n,error);check(n.mask==L"255.255.255.0",L"/24 dotted mask");
    auto secret=Protect(L"test-password");check(!secret.empty()&&secret!=L"test-password",L"DPAPI stores ciphertext");check(Unprotect(secret)==L"test-password",L"DPAPI round trip");
    wchar_t temp[MAX_PATH]{};GetTempPathW(MAX_PATH,temp);std::wstring path=std::wstring(temp)+L"MyVpnApp-native-test-routes.json";DeleteFileW(path.c_str());g.routesPath=path;g.routes={{L"id-a",L"192.0.2.0/24",L"Office",L"Not applied",true},{L"id-b",L"198.51.100.1/32",L"Host",L"Not applied",false}};check(SaveRoutes(),L"atomic route save");
    g.routes.clear();check(LoadRoutes()&&g.routes.size()==2&&g.routes[0].destination==L"192.0.2.0/24"&&!g.routes[1].enabled,L"route persistence across reload");
    {std::ofstream bad(path,std::ios::binary|std::ios::trunc);bad<<"[{\"unfinished\":";}std::wstring recovered;LoadOrBackup(path,recovered);WIN32_FIND_DATAW fd{};HANDLE find=FindFirstFileW((path+L".corrupt-*").c_str(),&fd);check(recovered.empty()&&find!=INVALID_HANDLE_VALUE,L"corrupt file backup and recovery");if(find!=INVALID_HANDLE_VALUE){FindClose(find);DeleteFileW((path+fd.cFileName).c_str());}DeleteFileW(path.c_str());DeleteFileW((path+L".tmp").c_str());
    return failures?1:0;
}
