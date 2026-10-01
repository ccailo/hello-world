# FlightVR Stereo 2.0

Cliente Android instalável para usar o celular em suporte tipo Cardboard como visor/head tracker do MSFS 2020.

- Head tracking 3DoF por Rotation Vector/giroscópio.
- UDP para OpenTrack: x, y, z, yaw, pitch, roll como 6 doubles little-endian.
- 30–120 Hz, padrão 90 Hz.
- VOL+ recentraliza.
- Visor SBS fullscreen via WebView.
- Teste estereoscópico interno com duas câmeras virtuais separadas por IPD.
- IPD ajustável de 50 a 78 mm.

Para estéreo real no MSFS, o PC deve fornecer duas vistas realmente diferentes no stream SBS. O app não transforma uma imagem mono em profundidade verdadeira.

OpenTrack: Input = UDP over network, porta 4242; Output = freetrack 2.0 Enhanced.
