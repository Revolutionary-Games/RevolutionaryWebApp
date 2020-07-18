# frozen_string_literal: true

module BuildOps
  # Clears all build of the day statuses
  class ClearBuildOfTheDay < Hyperstack::ServerOp
    param :acting_user
    validate { params.acting_user.admin? }
    step {
      Rails.logger.info "Clearing all BOTD statuses by #{params.acting_user.email}"

      DevBuild.where(build_of_the_day: true).each{|build|
        build.build_of_the_day = false
        build.save!
      }
    }
  end
end
