#!/usr/bin/env bash
# Clears all transactional and location data from the GodrejWMS database:
#   inventory (StockBatches), inward (GRNs, lines, put-aways), outward (pullouts, lines, picks),
#   inventory movements, and locations (PalletPositions, Racks).
#
# Kept: materials, design types, seasons, zone/location types, subtypes, movement types and reasons,
#       warehouses, user accounts, and the audit trail (ActivityLogs).
#
# A full backup is taken first. Restore with:
#   mysql -uroot GodrejWMS < backups/<file>.sql
#
# Usage (from the project root):  bash scripts/clean-data.sh
set -euo pipefail

DB="GodrejWMS"
MYSQL_USER="root"
BACKUP_DIR="backups"
BACKUP="$BACKUP_DIR/GodrejWMS-before-clean-$(date +%Y%m%d-%H%M%S).sql"

count() { mysql -u"$MYSQL_USER" "$DB" -N -e "SELECT COUNT(*) FROM $1"; }
TABLES="PulloutPicks PulloutTransactionLines PulloutTransactions InwardPutaways InwardTransactionLines InwardTransactions StockMovements StockBatches PalletPositions Racks"

echo "This will DELETE all rows from: $TABLES"
echo "Database: $DB. A backup is taken first."
read -r -p "Type YES to continue: " answer
[ "$answer" = "YES" ] || { echo "Cancelled."; exit 1; }

echo "Stopping the app (if running)..."
pkill -f "GodrejWMS.Web" || true
sleep 2

mkdir -p "$BACKUP_DIR"
mysqldump -u"$MYSQL_USER" --single-transaction --routines --triggers "$DB" > "$BACKUP"
[ -s "$BACKUP" ] || { echo "Backup failed, nothing was deleted."; exit 1; }
echo "Backup written: $BACKUP ($(du -h "$BACKUP" | cut -f1))"

echo "Counts before:"
for t in $TABLES; do printf "  %-26s %s\n" "$t" "$(count "$t")"; done

# Children first, all in one transaction so a failure leaves the data untouched.
mysql -u"$MYSQL_USER" "$DB" <<'SQL'
START TRANSACTION;
DELETE FROM PulloutPicks;
DELETE FROM PulloutTransactionLines;
DELETE FROM PulloutTransactions;
DELETE FROM InwardPutaways;
DELETE FROM InwardTransactionLines;
DELETE FROM InwardTransactions;
DELETE FROM StockMovements;
DELETE FROM StockBatches;
DELETE FROM PalletPositions;
DELETE FROM Racks;
COMMIT;
SQL

for t in $TABLES; do mysql -u"$MYSQL_USER" "$DB" -e "ALTER TABLE $t AUTO_INCREMENT=1"; done

echo "Counts after:"
for t in $TABLES; do printf "  %-26s %s\n" "$t" "$(count "$t")"; done
echo "Done. Start the app again with:"
echo "  dotnet run --project src/GodrejWMS.Web --launch-profile http"
