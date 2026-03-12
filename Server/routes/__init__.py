# [2026-03-12] Blueprint 註冊中心

def register_blueprints(app):
    from routes.status import status_bp
    from routes.machine import machine_bp
    from routes.program import program_bp
    from routes.tool import tool_bp
    from routes.probe import probe_bp
    from routes.atc import atc_bp
    from routes.config import config_bp
    from routes.stats import stats_bp

    app.register_blueprint(status_bp)
    app.register_blueprint(machine_bp)
    app.register_blueprint(program_bp)
    app.register_blueprint(tool_bp)
    app.register_blueprint(probe_bp)
    app.register_blueprint(atc_bp)
    app.register_blueprint(config_bp)
    app.register_blueprint(stats_bp)
