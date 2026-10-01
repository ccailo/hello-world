# FlightVR Bridge 1.0

Bootstrapper/central de configuração para usar um smartphone como HMD estereoscópico no Microsoft Flight Simulator 2020.

## Motor VR utilizado
- ALVR Streamer v20.8.0
- PhoneVR v2.0.0-beta
- SteamVR / OpenXR

A escolha de ALVR 20.8.0 é intencional: a documentação atual do PhoneVR declara essa versão como suportada.

## O que o EXE faz
- baixa ALVR e PhoneVR de releases oficiais do GitHub;
- extrai ALVR em %LOCALAPPDATA%\FlightVRBridge;
- detecta Steam/SteamVR;
- permite definir SteamVR como OpenXR runtime, com elevação só para a escrita do registro;
- abre ALVR e SteamVR;
- tenta instalar PhoneVR via ADB, se ADB estiver disponível;
- mostra IPs locais e diagnóstico;
- mantém os downloads em pasta local para instalação manual do APK.

## Uso
1. Execute FlightVR-Bridge-Setup.exe.
2. Clique INSTALAR MOTOR VR.
3. Instale o APK PhoneVR no celular.
4. Abra PhoneVR e selecione ALVR.
5. Abra ALVR Dashboard, confie no cliente do celular.
6. Defina SteamVR como OpenXR runtime.
7. Abra SteamVR e então MSFS 2020.
8. No simulador, use Ctrl+Tab para entrar/sair do VR.

Wi-Fi 5 GHz é recomendado, com o PC por Ethernet quando possível.
