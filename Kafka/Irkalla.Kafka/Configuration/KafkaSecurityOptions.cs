using Confluent.Kafka;

namespace Irkalla.Kafka.Configuration
{
    /// <summary>
    /// First-class SSL/TLS and SASL settings for connecting to secured Kafka clusters.
    /// <para>
    /// The TLS handshake, encryption and certificate validation are performed by librdkafka
    /// (Confluent.Kafka) — Irkalla.Kafka only forwards these settings. Anything not surfaced here
    /// can still be set via <see cref="IrkallaKafkaOptions.RawConfig"/> (raw key/value) or the
    /// <c>Configure*</c> callbacks, so no librdkafka setting is ever out of reach.
    /// </para>
    /// Only non-null values are applied, so this composes with the raw dictionary and callbacks.
    /// </summary>
    public class KafkaSecurityOptions
    {
        /// <summary>
        /// Security protocol: Plaintext (default), Ssl, SaslPlaintext, or SaslSsl.
        /// Set to <see cref="Confluent.Kafka.SecurityProtocol.Ssl"/> or
        /// <see cref="Confluent.Kafka.SecurityProtocol.SaslSsl"/> to enable TLS encryption.
        /// </summary>
        public SecurityProtocol? SecurityProtocol { get; set; }

        /// <summary>SASL mechanism (Plain, ScramSha256, ScramSha512, Gssapi, OAuthBearer).</summary>
        public SaslMechanism? SaslMechanism { get; set; }

        /// <summary>SASL username (for Plain / SCRAM).</summary>
        public string? SaslUsername { get; set; }

        /// <summary>SASL password (for Plain / SCRAM).</summary>
        public string? SaslPassword { get; set; }

        /// <summary>Path to the CA certificate file(s) used to verify the broker.</summary>
        public string? SslCaLocation { get; set; }

        /// <summary>Path to the client's public certificate (mutual TLS).</summary>
        public string? SslCertificateLocation { get; set; }

        /// <summary>Path to the client's private key (mutual TLS).</summary>
        public string? SslKeyLocation { get; set; }

        /// <summary>Password for the client's private key, if encrypted.</summary>
        public string? SslKeyPassword { get; set; }

        /// <summary>
        /// Whether to verify the broker's certificate against the CA. Default (null) leaves the
        /// librdkafka default (true). Set false only for local/test brokers with self-signed certs.
        /// </summary>
        public bool? EnableSslCertificateVerification { get; set; }

        /// <summary>
        /// Applies every non-null setting to a client config. Works for consumer, producer and
        /// admin configs (all derive from <see cref="ClientConfig"/>).
        /// </summary>
        internal void ApplyTo(ClientConfig config)
        {
            if (SecurityProtocol.HasValue) config.SecurityProtocol = SecurityProtocol;
            if (SaslMechanism.HasValue) config.SaslMechanism = SaslMechanism;
            if (SaslUsername != null) config.SaslUsername = SaslUsername;
            if (SaslPassword != null) config.SaslPassword = SaslPassword;
            if (SslCaLocation != null) config.SslCaLocation = SslCaLocation;
            if (SslCertificateLocation != null) config.SslCertificateLocation = SslCertificateLocation;
            if (SslKeyLocation != null) config.SslKeyLocation = SslKeyLocation;
            if (SslKeyPassword != null) config.SslKeyPassword = SslKeyPassword;
            if (EnableSslCertificateVerification.HasValue)
                config.EnableSslCertificateVerification = EnableSslCertificateVerification;
        }
    }
}
