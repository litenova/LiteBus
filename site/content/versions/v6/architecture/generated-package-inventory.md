# Generated Package Inventory

This file is generated from evaluated MSBuild metadata by `scripts/Get-PackageInventory.ps1`. Do not edit the table by hand.

| Package | Direct project references | Direct package references |
| --- | --- | --- |
| `LiteBus` | `LiteBus.Commands`, `LiteBus.Commands.Abstractions`, `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Events`, `LiteBus.Events.Abstractions`, `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Messaging`, `LiteBus.Messaging.Abstractions`, `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Queries`, `LiteBus.Queries.Abstractions` | none |
| `LiteBus.Analyzers` | none | `Microsoft.CodeAnalysis.Analyzers (private)`, `Microsoft.CodeAnalysis.CSharp (private)`, `System.Collections.Immutable (private)` |
| `LiteBus.Commands` | `LiteBus.Commands.Abstractions`, `LiteBus.Messaging`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Commands.Abstractions` | `LiteBus.Messaging.Abstractions` | none |
| `LiteBus.Commands.Extensions.Autofac` | `LiteBus.Commands`, `LiteBus.Runtime.Extensions.Autofac` | none |
| `LiteBus.Commands.Extensions.Microsoft.DependencyInjection` | `LiteBus.Commands`, `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` | none |
| `LiteBus.DurableMessaging.Abstractions` | `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Events` | `LiteBus.Events.Abstractions`, `LiteBus.Messaging`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Events.Abstractions` | `LiteBus.Messaging.Abstractions` | none |
| `LiteBus.Events.Extensions.Autofac` | `LiteBus.Events`, `LiteBus.Runtime.Extensions.Autofac` | none |
| `LiteBus.Events.Extensions.Microsoft.DependencyInjection` | `LiteBus.Events`, `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` | none |
| `LiteBus.Extensions.AspNetCore` | `LiteBus.Inbox.Abstractions`, `LiteBus.Outbox.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Extensions.Diagnostics.HealthChecks` | `LiteBus.Runtime.Abstractions` | `Microsoft.Extensions.Diagnostics.HealthChecks` |
| `LiteBus.Extensions.Microsoft.DependencyInjection` | `LiteBus.Commands.Extensions.Microsoft.DependencyInjection`, `LiteBus.Events.Extensions.Microsoft.DependencyInjection`, `LiteBus.Messaging.Extensions.Microsoft.DependencyInjection`, `LiteBus.Queries.Extensions.Microsoft.DependencyInjection` | none |
| `LiteBus.Inbox` | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Inbox.Abstractions`, `LiteBus.Messaging`, `LiteBus.Runtime.Abstractions` | `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Inbox.Abstractions` | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Inbox.Dispatch` | `LiteBus.Inbox.Abstractions`, `LiteBus.Messaging`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | none |
| `LiteBus.Inbox.Dispatch.Amqp` | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Dispatch`, `LiteBus.Transport.Amqp` | none |
| `LiteBus.Inbox.Dispatch.AwsSqs` | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Dispatch`, `LiteBus.Transport.AwsSqs` | none |
| `LiteBus.Inbox.Dispatch.AzureServiceBus` | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Dispatch`, `LiteBus.Transport.AzureServiceBus` | none |
| `LiteBus.Inbox.Dispatch.InMemory` | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Dispatch`, `LiteBus.Transport.InMemory` | none |
| `LiteBus.Inbox.Dispatch.InProcess` | `LiteBus.Commands`, `LiteBus.Commands.Abstractions`, `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Messaging`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Inbox.Dispatch.Kafka` | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Dispatch`, `LiteBus.Transport.Kafka` | none |
| `LiteBus.Inbox.Extensions.OpenTelemetry` | `LiteBus.Inbox` | `OpenTelemetry` |
| `LiteBus.Inbox.Ingress` | `LiteBus.Inbox.Abstractions`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Inbox.Ingress.Amqp` | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Ingress`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport.Amqp` | `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Inbox.Ingress.AwsSqs` | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Ingress`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport.AwsSqs` | none |
| `LiteBus.Inbox.Ingress.AzureServiceBus` | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Ingress`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport.AzureServiceBus` | none |
| `LiteBus.Inbox.Ingress.InMemory` | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Ingress`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport.InMemory` | none |
| `LiteBus.Inbox.Ingress.Kafka` | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Ingress`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport.Kafka` | none |
| `LiteBus.Inbox.Storage.EntityFrameworkCore` | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Storage.EntityFrameworkCore` | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational` |
| `LiteBus.Inbox.Storage.InMemory` | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Inbox.Storage.PostgreSql` | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Storage.PostgreSql` | `Npgsql` |
| `LiteBus.Messaging` | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions` | `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Messaging.Abstractions` | `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Messaging.Extensions.Autofac` | `LiteBus.Messaging`, `LiteBus.Runtime.Extensions.Autofac` | none |
| `LiteBus.Messaging.Extensions.Microsoft.DependencyInjection` | `LiteBus.Messaging`, `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` | none |
| `LiteBus.Outbox` | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Messaging`, `LiteBus.Outbox.Abstractions`, `LiteBus.Runtime.Abstractions` | `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Outbox.Abstractions` | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Outbox.Dispatch` | `LiteBus.Messaging`, `LiteBus.Outbox.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | none |
| `LiteBus.Outbox.Dispatch.Amqp` | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox.Dispatch`, `LiteBus.Transport.Amqp` | none |
| `LiteBus.Outbox.Dispatch.AwsSqs` | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox.Dispatch`, `LiteBus.Transport.AwsSqs` | none |
| `LiteBus.Outbox.Dispatch.AzureServiceBus` | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox.Dispatch`, `LiteBus.Transport.AzureServiceBus` | none |
| `LiteBus.Outbox.Dispatch.InMemory` | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox.Dispatch`, `LiteBus.Transport.InMemory` | none |
| `LiteBus.Outbox.Dispatch.InProcess` | `LiteBus.Events`, `LiteBus.Events.Abstractions`, `LiteBus.Messaging`, `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Outbox.Dispatch.Kafka` | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox.Dispatch`, `LiteBus.Transport.Kafka` | none |
| `LiteBus.Outbox.Extensions.OpenTelemetry` | `LiteBus.Outbox` | `OpenTelemetry` |
| `LiteBus.Outbox.Storage.EntityFrameworkCore` | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Storage.EntityFrameworkCore` | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational` |
| `LiteBus.Outbox.Storage.InMemory` | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Outbox.Storage.PostgreSql` | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Storage.PostgreSql` | `Npgsql` |
| `LiteBus.Queries` | `LiteBus.Messaging`, `LiteBus.Queries.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Queries.Abstractions` | `LiteBus.Messaging.Abstractions` | none |
| `LiteBus.Queries.Extensions.Autofac` | `LiteBus.Queries`, `LiteBus.Runtime.Extensions.Autofac` | none |
| `LiteBus.Queries.Extensions.Microsoft.DependencyInjection` | `LiteBus.Queries`, `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` | none |
| `LiteBus.Runtime` | `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Runtime.Abstractions` | none | none |
| `LiteBus.Runtime.Extensions.Autofac` | `LiteBus.Runtime`, `LiteBus.Runtime.Extensions.Autofac.Hosting` | `Autofac`, `Autofac.Extensions.DependencyInjection` |
| `LiteBus.Runtime.Extensions.Autofac.Hosting` | `LiteBus.Runtime.Extensions.Hosting` | `Autofac`, `Microsoft.Extensions.Hosting.Abstractions` |
| `LiteBus.Runtime.Extensions.Hosting` | `LiteBus.Runtime.Abstractions` | `Microsoft.Extensions.Hosting.Abstractions` |
| `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` | `LiteBus.Runtime`, `LiteBus.Runtime.Extensions.Microsoft.Hosting` | `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `LiteBus.Runtime.Extensions.Microsoft.Hosting` | `LiteBus.Runtime.Extensions.Hosting` | `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions` |
| `LiteBus.Saga` | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Saga.Abstractions` | `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Saga.Abstractions` | `LiteBus.Commands.Abstractions`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Saga.InboxIntegration` | `LiteBus.Commands.Abstractions`, `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Messaging`, `LiteBus.Saga` | none |
| `LiteBus.Saga.Storage.PostgreSql` | `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Saga`, `LiteBus.Saga.Abstractions`, `LiteBus.Storage.PostgreSql` | `Npgsql` |
| `LiteBus.Storage.EntityFrameworkCore` | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Messaging.Abstractions` | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational` |
| `LiteBus.Storage.PostgreSql` | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Messaging.Abstractions` | `Npgsql` |
| `LiteBus.Storage.Testing` | `LiteBus.Inbox.Abstractions`, `LiteBus.Outbox.Abstractions` | `AwesomeAssertions`, `xunit` |
| `LiteBus.Testing` | none | none |
| `LiteBus.Testing.DurableMessaging` | `LiteBus.Extensions.Microsoft.DependencyInjection`, `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Storage.InMemory`, `LiteBus.Messaging`, `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox.Storage.InMemory`, `LiteBus.Runtime.Abstractions` | `Microsoft.Extensions.DependencyInjection` |
| `LiteBus.Testing.Hosting` | `LiteBus.Inbox`, `LiteBus.Runtime.Abstractions`, `LiteBus.Runtime.Extensions.Microsoft.Hosting` | `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting.Abstractions` |
| `LiteBus.Testing.Mediation` | `LiteBus.Commands.Abstractions`, `LiteBus.Events.Abstractions`, `LiteBus.Queries.Abstractions` | none |
| `LiteBus.Testing.Transport` | `LiteBus.Transport.Abstractions` | none |
| `LiteBus.Transport` | `LiteBus.Runtime.Abstractions`, `LiteBus.Transport.Abstractions` | none |
| `LiteBus.Transport.Abstractions` | none | none |
| `LiteBus.Transport.Amqp` | `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | `RabbitMQ.Client` |
| `LiteBus.Transport.Amqp.Extensions.OpenTelemetry` | `LiteBus.Transport.Amqp` | `OpenTelemetry` |
| `LiteBus.Transport.AwsSqs` | `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | `AWSSDK.SQS` |
| `LiteBus.Transport.AzureServiceBus` | `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | `Azure.Messaging.ServiceBus`, `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Transport.Extensions.OpenTelemetry` | `LiteBus.Transport` | `OpenTelemetry` |
| `LiteBus.Transport.InMemory` | `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | none |
| `LiteBus.Transport.Kafka` | `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | `Confluent.Kafka` |
| `LiteBus.Transport.Testing` | `LiteBus.Transport.Abstractions` | `xunit` |
