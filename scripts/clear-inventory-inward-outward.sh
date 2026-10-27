#!/usr/bin/env bash
# Clears production stock-on-hand, inward, outward and inventory-adjustment data from GodrejWMS,
# for ALL warehouses. Takes a full mysqldump backup first, shows row counts, requires typing
# "YES" to proceed, then deletes inside one transaction.
#
# Kept untouched: Materials, Racks, Locations, Warehouses/Regions/etc., the Inventory Movement
# log (StockMovements), the Audit Trail (ActivityLogs), and all user/role data.
#
# Usage:
#   DB_HOST=prod-db.internal DB_USER=app_admin DB_NAME=GodrejWMS \
#     bash scripts/clear-inventory-inward-outward.sh
#
# You'll be prompted for the DB password (or set DB_PASSWORD, less secure - visible in your shell
# history/process list). DB_PORT defaults to 3306 if unset.
set -euo pipefail

DB_HOST="${DB_HOST:?Set DB_HOST to the production MySQL host}"
DB_PORT="${DB_PORT:-3306}"
DB_USER="${DB_USER:?Set DB_USER to the production MySQL user}"
DB_NAME="${DB_NAME:?Set DB_NAME (usually GodrejWMS)}"
BACKUP_DIR="${BACKUP_DIR:-backups}"
BACKUP="$BACKUP_DIR/${DB_NAME}-before-clear-$(date +%Y%m%d-%H%M%S).sql"

if [ -z "${DB_PASSWORD:-}" ]; then
  read -r -s -p "MySQL password for $DB_USER@$DB_HOST: " DB_PASSWORD
  echo
fi

MYSQL_ARGS=(-h"$DB_HOST" -P"$DB_PORT" -u"$DB_USER" -p"$DB_PASSWORD")

count() { mysql "${MYSQL_ARGS[@]}" "$DB_NAME" -N -e "SELECT COUNT(*) FROM $1"; }
TABLES="PulloutPicks PulloutTransactionLines PulloutTransactions InwardPutaways InwardTransactionLines InwardTransactions InventoryAdjustmentLines InventoryAdjustments StockBatches"

echo "Target: $DB_USER@$DB_HOST:$DB_PORT/$DB_NAME"
echo "This will DELETE all rows from: $TABLES"
echo "Kept untouched: Materials, Racks, Locations, StockMovements, ActivityLogs, and everything else."
echo
echo "Counts before:"
for t in $TABLES; do printf "  %-26s %s\n" "$t" "$(count "$t")"; done
echo
read -r -p "A full backup will be taken first. Type YES to continue: " answer
[ "$answer" = "YES" ] || { echo "Cancelled. Nothing was touched."; exit 1; }

mkdir -p "$BACKUP_DIR"
echo "Taking backup..."
mysqldump -h"$DB_HOST" -P"$DB_PORT" -u"$DB_USER" -p"$DB_PASSWORD" \
  --single-transaction --routines --triggers "$DB_NAME" > "$BACKUP"
[ -s "$BACKUP" ] || { echo "Backup failed or is empty - aborting, nothing was deleted."; exit 1; }
echo "Backup written: $BACKUP ($(du -h "$BACKUP" | cut -f1))"
echo "Restore with: mysql -h$DB_HOST -P$DB_PORT -u$DB_USER -p $DB_NAME < $BACKUP"
echo

read -r -p "Backup confirmed OK. Type YES again to run the deletes now: " answer2
[ "$answer2" = "YES" ] || { echo "Cancelled before deleting. Backup was still taken, nothing was deleted."; exit 1; }

mysql "${MYSQL_ARGS[@]}" "$DB_NAME" <<'SQL'
START TRANSACTION;
DELETE FROM PulloutPicks;
DELETE FROM PulloutTransactionLines;
DELETE FROM PulloutTransactions;
DELETE FROM InwardPutaways;
DELETE FROM InwardTransactionLines;
DELETE FROM InwardTransactions;
DELETE FROM InventoryAdjustmentLines;
DELETE FROM InventoryAdjustments;
DELETE FROM StockBatches;
COMMIT;
SQL

echo
echo "Counts after:"
for t in $TABLES; do printf "  %-26s %s\n" "$t" "$(count "$t")"; done
echo "Done. Backup kept at: $BACKUP"
