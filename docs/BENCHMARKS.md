# TwitchCraft Benchmarks

Performance testing for TwitchCraft.

> **TL;DR:** TwitchCraft itself is lightweight. In the latest minimized benchmark, it averaged roughly **171–182 MB RAM** and **0.01–0.08% CPU** depending on the selected settings. The Minecraft Java server is a much larger part of total system memory use. For ordinary use, TwitchCraft generally stays around **150–185 MB of RAM** and uses a very small amount of CPU. If you are running TwitchCraft on the same PC as Minecraft and streaming software, **Low Resource Mode** is the best option when you want to minimize background overhead.

## Recommended use

| Configuration | Best for |
| --- | --- |
| **Default settings** | Most users |
| **Low Resource Mode** | Lower-end PCs, single-PC streaming, or keeping TwitchCraft minimized |

**Low Resource Mode is the only selectable performance mode in TwitchCraft.** The **Minimum**, **Normal**, and **Maximum** labels used below are benchmark configurations created to compare different groups of settings. **Normal** represents the default configuration, while **Minimum** and **Maximum** represent deliberately reduced and increased benchmark settings.

These results are from controlled tests on one Windows PC and should be treated as comparative measurements, not guaranteed usage on every system.

---

## 1. TwitchCraft-only benchmark

This test isolated the TwitchCraft application itself. The Minecraft Java server and live Twitch networking were intentionally excluded.

### Idle, window visible

| Settings | Average CPU | Average RAM |
| --- | ---: | ---: |
| Maximum | 0.03% | 151 MB |
| Normal | ~0.00% | 151 MB |
| Low Resource | ~0.00% | 151 MB |
| Minimum | ~0.00% | 150 MB |

At idle, all four benchmark configurations were effectively identical.

### Extreme visible-console workload

The app was given a deliberately extreme stress workload consisting of:

- 400 simulated Minecraft log lines per second
- 200 simulated Twitch log lines per second
- a changing 500-viewer roster every second
- 5 statistics events per second

| Settings | Average CPU | Average RAM | Peak RAM |
| --- | ---: | ---: | ---: |
| Maximum | 2.19% | 207 MB | 221 MB |
| Normal | 0.29% | 189 MB | 202 MB |
| Low Resource | 0.34% | 184 MB | 192 MB |
| Minimum | 0.22% | 183 MB | 192 MB |

Maximum became noticeably more expensive when extremely large amounts of log activity were continuously rendered on screen.

### Same workload while minimized

| Settings | Average CPU | Average RAM | Peak RAM |
| --- | ---: | ---: | ---: |
| Maximum | 2.02% | 169 MB | 183 MB |
| Normal | 0.20% | 155 MB | 164 MB |
| Low Resource | **0.01%** | **133 MB** | 135 MB |
| Minimum | **0.01%** | **133 MB** | 135 MB |

This was the clearest demonstration of Low Resource Mode's benefit. Under the same extreme workload while minimized, Low Resource used about **23 MB less average RAM** than Normal and reduced CPU from about **0.20% to 0.01%**.

---

## 2. Real Twitch + real Minecraft server idle benchmark

This test used:

- a real Twitch IRC/Helix connection
- a real Minecraft 26.2 Java server
- JDK 25
- TwitchCraft open normally
- no synthetic workload

| Settings | TwitchCraft CPU | TwitchCraft RAM | Java RAM | Total RAM |
| --- | ---: | ---: | ---: | ---: |
| Maximum | 0.035% | 178 MB | 1,100 MB | 1,277 MB |
| Normal | 0.035% | 177 MB | 1,099 MB | 1,276 MB |
| Low Resource | 0.024% | 177 MB | 1,100 MB | 1,277 MB |
| Minimum | 0.003% | 179 MB | 1,096 MB | 1,275 MB |

When everything was idle, changing TwitchCraft settings had almost no effect on total memory usage. The Java server accounted for most of the combined footprint.

---

## 3. Comprehensive 72-run benchmark

This was the largest benchmark and tested:

- 4 TwitchCraft benchmark configurations: Minimum, Low Resource, Normal, and Maximum
- 3 Minecraft server RAM allocations: 4 GB, 8 GB, and 16 GB
- 3 workload levels: Low, Normal, and Heavy
- 2 repetitions of every combination
- **72 measured runs total**
- 120 seconds per measured run
- a fresh TwitchCraft process for every run
- TwitchCraft minimized during measurement
- a real local Minecraft Java server
- deterministic local mock Twitch traffic

### Overall TwitchCraft results

| Settings | Average CPU | Average RAM | Highest observed RAM peak |
| --- | ---: | ---: | ---: |
| Minimum | **0.01%** | **171 MB** | 179 MB |
| Low Resource | **0.02%** | **173 MB** | 183 MB |
| Normal | 0.07% | 181 MB | 191 MB |
| Maximum | 0.08% | 182 MB | 191 MB |

All four benchmark configurations remained very lightweight.

Low Resource was the cleanest direct comparison with Normal because the benchmark's Low Resource configuration was the Normal/default configuration with only **Low Resource Mode enabled**. This makes it the best measurement of what enabling the actual Low Resource Mode changes.

### Effect of Minecraft server RAM

| Server allocation | TwitchCraft RAM | Java server RAM |
| --- | ---: | ---: |
| 4 GB | 177 MB | 1,155 MB |
| 8 GB | 177 MB | 1,236 MB |
| 16 GB | 177 MB | 1,375 MB |

Increasing the Minecraft server's configured RAM had basically no effect on TwitchCraft's own footprint. It mainly increased the Java server's memory use.

These figures do **not** mean a 16 GB server allocation constantly consumes 16 GB of physical RAM. They show the measured working set during this benchmark.

### Effect of workload

| Workload | TwitchCraft CPU | TwitchCraft RAM |
| --- | ---: | ---: |
| Low | 0.04% | 176 MB |
| Normal | 0.05% | 177 MB |
| Heavy | 0.06% | 178 MB |

Moving from Low to Heavy synthetic Twitch traffic increased TwitchCraft's resource use only slightly.

### Limitations

These benchmarks are intended mainly for comparing TwitchCraft CPU and RAM usage under controlled conditions. Results will vary by hardware, Minecraft version, server workload, mods, player count, and other software running at the same time.


---

## Bottom line

Across all completed tests, TwitchCraft remained lightweight. The default settings are appropriate for most users, while **Low Resource Mode** provides the clearest resource savings when TwitchCraft is minimized. The Minecraft Java server generally contributes far more to total system memory use than TwitchCraft itself.

The **Minimum** and **Maximum** results are benchmark configurations for comparison, not selectable TwitchCraft modes.
