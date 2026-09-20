import qrcode from 'qrcode-generator';

// Black-on-white QR as inline SVG (the caller supplies the white quiet-zone background).
export function qrSvg(text) {
  const qr = qrcode(0, 'M');
  qr.addData(text);
  qr.make();
  return qr.createSvgTag({ cellSize: 1, margin: 2, scalable: true });
}
