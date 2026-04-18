# AWS EC2 & MySQL Setup Guide

This guide explains how to set up an AWS EC2 instance, configure a MySQL database for public access, and connect securely using an SSH tunnel.

---

## 1. Launch an EC2 Instance

1. Log in to the [AWS Management Console](https://console.aws.amazon.com/).
2. Go to **EC2** > **Instances** > **Launch Instance**.
3. Choose an Amazon Machine Image (AMI), e.g., Ubuntu Server.
4. Select an instance type (e.g., t2.micro for testing).
5. Configure instance details as needed.
6. Add storage if required.
7. Add tags (optional).
8. Configure the security group:
   - Allow SSH (port 22) from your IP.
   - Allow any other required ports.
9. Launch the instance and download the PEM key (e.g., `AWS KEY - ASIA.pem`).

---

## 2. Set Up a MySQL Database (RDS)

1. Go to **RDS** > **Create database**.
2. Select **MySQL** as the engine.
3. Choose a DB instance size.
4. Set DB credentials (username, password).
5. In **Connectivity**:
   - Set **Public access** to **Yes** (for testing; for production, use private access and SSH tunneling).
   - Choose or create a security group that allows inbound MySQL (port 3306) from your EC2 or your IP.
6. Launch the database.
7. Note the **Endpoint** (e.g., `database-1.cdqekokcacs0.ap-southeast-1.rds.amazonaws.com`).

---

## 3. (Recommended) Restrict Public Access
- For security, restrict inbound rules to only trusted IPs or use SSH tunneling instead of public access.

---

## 4. Connect via SSH Tunnel

**On your local machine, run:**

```powershell
ssh -i "C:\Users\USER\Desktop\AWS KEY - ASIA.pem" -L 3307:database-1.cdqekokcacs0.ap-southeast-1.rds.amazonaws.com:3306 ubuntu@13.212.236.252 -N
```

- Replace the PEM path, RDS endpoint, and EC2 public IP as needed.
- This forwards your local port 3307 to the remote MySQL port 3306.

**To connect to MySQL locally:**
- Use `localhost:3307` as the host in your MySQL client.

---

## 5. Troubleshooting
- Ensure your security groups allow the necessary traffic.
- The EC2 instance must be in the same VPC/subnet as the RDS instance or have network access.
- The PEM file must have correct permissions (`chmod 400` on Linux/macOS).

---

## 6. References
- [AWS EC2 Documentation](https://docs.aws.amazon.com/ec2/)
- [AWS RDS Documentation](https://docs.aws.amazon.com/rds/)
- [SSH Tunneling Guide](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/USER_ConnectToInstance.html)
