CREATE TABLE ${TENANT_RESERVED_NAME}.jobs (
    job_id INT IDENTITY(1,1) PRIMARY KEY,
    job_title VARCHAR (35) NOT NULL,
    min_salary DECIMAL (8, 2) DEFAULT NULL,
    max_salary DECIMAL (8, 2) DEFAULT NULL
);
 
CREATE TABLE ${TENANT_RESERVED_NAME}.departments (
    department_id INT IDENTITY(1,1) PRIMARY KEY,
    department_name VARCHAR (30) NOT NULL,
    location_id INT DEFAULT NULL,
    FOREIGN KEY (location_id) REFERENCES ${TENANT_RESERVED_NAME}.locations (location_id)
);