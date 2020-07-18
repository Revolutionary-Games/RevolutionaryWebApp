# frozen_string_literal: true

module BuildOps
  # Updates description of a build
  class UpdateDescription < Hyperstack::ServerOp
    param :acting_user
    param :build_id
    param :description
    validate { params.acting_user.developer? }
    add_error(:build_id, :does_not_exist, 'build does not exist') {
      !(@build = DevBuild.find_by_id(params.build_id))
    }
    step {
      @build.description = params.description
      @build.save!

      Rails.logger.info "Build #{@build.id} description set to: #{params.description} "\
                        "by #{params.acting_user.email}"
    }
  end
end
