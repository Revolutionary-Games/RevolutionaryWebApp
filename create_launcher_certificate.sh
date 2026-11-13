#!/usr/bin/env bash
set -euo pipefail

# Keep the private key and exported PKCS#12 file accessible only to their owner.
umask 077

# Configurable parameters
CN="${CN:-production.dev.revolutionarygamesstudio}"
COUNTRY="${COUNTRY:-FI}"
STATE="${STATE:-State}"
LOCALITY="${LOCALITY:-City}"
ORG="${ORG:-Revolutionary Games Studio}"
OU="${OU:-DC}"

KEY_SIZE="${KEY_SIZE:-4096}"
DAYS_VALID="${DAYS_VALID:-1825}"        # 5 years
START_IN_DAYS="${START_IN_DAYS:-30}"    # Certificate starts being valid 30 days from now

KEY_FILE="${KEY_FILE:-cert.key}"
CSR_FILE="${CSR_FILE:-cert.csr}"
CERT_FILE="${CERT_FILE:-cert.crt}"
P12_FILE="${P12_FILE:-cert.p12}"

OPENSSL_CONF_FILE="$(mktemp)"
CA_DB_DIR="$(mktemp -d)"

cleanup() {
  rm -f "$OPENSSL_CONF_FILE"
  rm -rf "$CA_DB_DIR"
}
trap cleanup EXIT

mkdir -p "$CA_DB_DIR/certs"
touch "$CA_DB_DIR/index.txt"

# openssl ca accepts explicit validity dates, including a future notBefore.
# No system clock changes are needed. Both dates use the same reference time.
NOW="$(date -u '+%Y-%m-%d %H:%M:%S UTC')"
START_DATE="$(date -u -d "$NOW +${START_IN_DAYS} days" '+%Y%m%d%H%M%SZ')"
END_DATE="$(date -u -d "$NOW +$((START_IN_DAYS + DAYS_VALID)) days" '+%Y%m%d%H%M%SZ')"

cat > "$OPENSSL_CONF_FILE" <<EOF
[ ca ]
default_ca = CA_default

[ CA_default ]
dir               = $CA_DB_DIR
database          = \$dir/index.txt
serial            = \$dir/serial
new_certs_dir     = \$dir/certs

private_key       = $KEY_FILE

default_md        = sha256
policy            = policy_any
x509_extensions   = v3_cert
copy_extensions   = copy
unique_subject    = no

[ policy_any ]
countryName             = optional
stateOrProvinceName     = optional
localityName            = optional
organizationName        = optional
organizationalUnitName  = optional
commonName              = supplied

[ req ]
default_bits        = $KEY_SIZE
default_md          = sha256
distinguished_name  = req_distinguished_name
prompt              = no

[ req_distinguished_name ]
C  = $COUNTRY
ST = $STATE
L  = $LOCALITY
O  = $ORG
OU = $OU
CN = $CN

[ v3_cert ]
basicConstraints = critical,CA:FALSE
keyUsage = critical,digitalSignature,keyEncipherment
extendedKeyUsage = serverAuth,clientAuth
subjectAltName = @alt_names
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid,issuer

[ alt_names ]
DNS.1 = $CN
EOF

openssl genrsa \
  -out "$KEY_FILE" \
  "$KEY_SIZE"

openssl req \
  -new \
  -key "$KEY_FILE" \
  -out "$CSR_FILE" \
  -config "$OPENSSL_CONF_FILE"

# Apply v3_cert here: its authorityKeyIdentifier requires an issuer and cannot
# be included in the CSR via req_extensions.
openssl ca \
  -selfsign \
  -rand_serial \
  -batch \
  -notext \
  -config "$OPENSSL_CONF_FILE" \
  -in "$CSR_FILE" \
  -out "$CERT_FILE" \
  -startdate "$START_DATE" \
  -enddate "$END_DATE" \
  -extensions v3_cert

openssl pkcs12 \
  -export \
  -inkey "$KEY_FILE" \
  -in "$CERT_FILE" \
  -out "$P12_FILE" \
  -name "$CN"

echo "Created:"
echo "  Private key:  $KEY_FILE"
echo "  CSR:          $CSR_FILE"
echo "  Certificate:  $CERT_FILE"
echo "  PKCS#12 file: $P12_FILE"
echo
echo "Certificate validity:"
openssl x509 -in "$CERT_FILE" -noout -dates
