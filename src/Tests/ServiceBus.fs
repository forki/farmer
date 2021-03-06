module ServiceBus

open Expecto
open Farmer
open Farmer.Arm.ServiceBus
open Farmer.Builders
open Farmer.ServiceBus
open Microsoft.Azure.Management.ServiceBus
open Microsoft.Azure.Management.ServiceBus.Models
open Microsoft.Rest
open System

/// Client instance needed to get the serializer settings.
let dummyClient = new ServiceBusManagementClient (Uri "http://management.azure.com", TokenCredentials "NotNullOrWhiteSpace")

let tests = testList "Service Bus Tests" [
    let getUnsafe data = data |> convertResourceBuilder (fun (ns:{| resources:obj list |}) -> ns.resources.[0]) dummyClient.SerializationSettings
    test "Namespace is correctly created" {
        let sbNs =
            arm {
                add_resource (
                    serviceBus {
                        name "serviceBus"
                        sku Standard
                    })
            }
            |> findAzureResources<SBNamespace> dummyClient.SerializationSettings
            |> List.head

        sbNs.Validate()

        Expect.equal sbNs.Name "serviceBus" "Invalid namespace name"
        Expect.equal sbNs.Sku.Name SkuName.Standard "Invalid Sku"
    }

    test "Namespace name length is respected" {
        Expect.throws(fun _ -> serviceBus { name "myns" } |> ignore) "Namespace length is too small"
        Expect.throws(fun _ -> serviceBus { name (String.replicate 51 "x") } |> ignore) "Namespace length is too long"
    }

    testList "Queue Tests" [
        test "Queue is correctly created" {
            let queue:SBQueue =
                serviceBus {
                    name "serviceBus"
                    sku ServiceBus.Standard
                    add_queues [
                        queue {
                            name "my-queue"
                            duplicate_detection_minutes 5
                            enable_dead_letter_on_message_expiration
                            enable_partition
                            enable_session
                            lock_duration_minutes 5
                            max_delivery_count 3
                            message_ttl_days 10
                        }
                    ]
                } |> getUnsafe

            Expect.equal queue.Name "my-queue" "Invalid queue name"
            Expect.isTrue (queue.RequiresDuplicateDetection.GetValueOrDefault false) "Duplicate detection should be enabled"
            Expect.equal queue.DuplicateDetectionHistoryTimeWindow (Nullable(TimeSpan(0, 5, 0))) "Duplicate detection window incorrect"
            Expect.isTrue (queue.DeadLetteringOnMessageExpiration.GetValueOrDefault false) "Dead lettering should be enabled"
            Expect.isTrue (queue.EnablePartitioning.GetValueOrDefault false) "Partitioning should be enabled"
            Expect.isTrue (queue.RequiresSession.GetValueOrDefault false) "Sessions should be enabled"
            Expect.equal queue.LockDuration (Nullable (TimeSpan(0, 5, 0))) "Lock duration incorrect"
            Expect.equal (queue.DefaultMessageTimeToLive.GetValueOrDefault TimeSpan.MinValue).TotalDays 10. "Default TTL incorrect"
            Expect.equal queue.MaxDeliveryCount (Nullable 3) "Max delivery count incorrect"
        }

        test "Cannot set duplicate detection on basic tier" {
            Expect.throws (fun () ->
                serviceBus {
                    name "serviceBus"
                    add_queues [
                        queue {
                            name "my-queue"
                            duplicate_detection_minutes 1
                        }
                    ]
                } |> ignore) "Duplicate detection isn't allowed on basic tier"
        }

        test "Cannot set lock duration more than 5 minutes" {
            Expect.throws (fun () ->
                serviceBus {
                    name "serviceBus"
                    add_queues [
                        queue {
                            name "my-queue"
                            lock_duration_minutes 6
                        }
                    ]
                } |> ignore) "Lock duration max should be 5 minutes"
        }

        test "Default TTL set for Basic queue" {
            let queue:SBQueue =
                serviceBus {
                    name "serviceBus"
                    add_queues [ queue { name "my-queue" } ]
                } |> getUnsafe

            Expect.equal (queue.DefaultMessageTimeToLive.GetValueOrDefault TimeSpan.MinValue).TotalDays 14. "Default TTL should be 14 days"
        }

        test "Default TTL set for Standard queue" {
            let queue:SBQueue =
                serviceBus {
                    name "serviceBus"
                    sku ServiceBus.Standard
                    add_queues [ queue { name "my-queue" } ]
                } |> getUnsafe

            Expect.equal (queue.DefaultMessageTimeToLive.GetValueOrDefault TimeSpan.MinValue).TotalDays TimeSpan.MaxValue.TotalDays "Default TTL should be max value"
        }

        test "Correctly creates multiple queues" {
            let theBus = serviceBus {
                name "serviceBus"
                add_queues [
                    queue { name "queue-a" }
                    queue { name "queue-b" }
                ]
            }
            let deployment = arm { add_resource theBus }
            match deployment.Template.Resources with
            | [ :? Namespace as ns ] when ns.Queues.Length = 2 -> ()
            | _ -> failwith "Should have two queues in a single namespace."
        }
    ]

    testList "Topic Tests" [
        test "Create create a basic topic" {
            let topic:SBTopic =
                serviceBus {
                    name "servicebus"
                    add_topics [
                        topic {
                            name "topic"
                            duplicate_detection_minutes 3
                            message_ttl_days 2
                            enable_partition
                        }
                    ]
                } |> getUnsafe
            Expect.equal topic.Name "topic" "Name not set"
            Expect.equal topic.RequiresDuplicateDetection (Nullable true) "Duplicate detection not set"
            Expect.equal topic.DuplicateDetectionHistoryTimeWindow (Nullable (TimeSpan.FromMinutes 3.)) "Duplicate detection time not set"
            Expect.equal topic.DefaultMessageTimeToLive (Nullable (TimeSpan.FromDays 2.)) "Time to live not set"
            Expect.equal topic.EnablePartitioning (Nullable true) "Paritition not set"
        }
    ]
]