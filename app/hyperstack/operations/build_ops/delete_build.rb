# frozen_string_literal: true

module BuildOps
  # Deletes a build
  class DeleteBuild < Hyperstack::ServerOp
    param :acting_user
    param :build_id
    # Has to be at least a developer
    validate { params.acting_user.developer? }
    add_error(:build_id, :does_not_exist, 'build does not exist') {
      !(@build = DevBuild.find_by_id(params.build_id))
    }
    # Only admins can delete important builds
    validate { params.acting_user.admin? ||
      (!@build.keep && !@build.important && !@build.build_of_the_day) }
    step {
      Rails.logger.info "Build #{@build.id} deleted by #{params.acting_user.email}"
      @build.destroy!
    }
  end
end
