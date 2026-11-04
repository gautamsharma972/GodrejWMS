import { Directory, File, Paths } from "expo-file-system";
import * as Sharing from "expo-sharing";

const XLSX_MIME = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

/** Writes downloaded bytes to the cache directory and opens the device share sheet - the mobile
 * equivalent of the web app's "Download Excel" button (which triggers a browser file download;
 * there's no such concept on-device, so sharing/saving via the OS share sheet is the natural
 * replacement). */
export async function saveAndShareXlsx(bytes: Uint8Array, fileName: string): Promise<void> {
  const dir = new Directory(Paths.cache, "godrejwms-downloads");
  if (!dir.exists) {
    dir.create({ intermediates: true });
  }

  const file = new File(dir, fileName);
  if (file.exists) {
    file.delete();
  }
  file.create();
  await file.write(bytes);

  if (await Sharing.isAvailableAsync()) {
    await Sharing.shareAsync(file.uri, { mimeType: XLSX_MIME, dialogTitle: fileName });
  }
}
