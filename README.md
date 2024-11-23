# GelfFormatter for Serilog RabbitMQ Sink

This GelfFormatter is designed to fill a gap in Serilog's RabbitMQ sink configuration in order to use the GELF AMQP input on Graylog. It implements the ITextFormatter interface and is primarily intended for use with RabbitMQ sinks. (but hey, you do you. I needed it for that)

The formatter structures the log events in the GELF (Graylog Extended Log Format) compatible format, making it easier to integrate with systems like Graylog that use GELF for logging over AMQP.
