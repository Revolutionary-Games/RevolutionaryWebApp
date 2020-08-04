# frozen_string_literal: true

module BuildOps
  # Makes a build the build of the day
  class MakeBuildOfTheDay < Hyperstack::ServerOp
    param :acting_user
    param :build_id
    validate { params.acting_user.developer? }
    add_error(:build_id, :does_not_exist, 'build does not exist') {
      !(@build = DevBuild.find_by_id(params.build_id))
    }
    # If anonymous and not verified can't be made BOTD
    validate { @build.verified || !@build.anonymous}
    # Must have a description
    add_error(:build_description, :is_blank, 'build does not have description') {
      @build.description.blank?
    }
    step {
      if @build.anonymous && !@build.verified
        raise "Can't make BODT out of an anonymous unverified build"
      end

      Rails.logger.info "Build #{@build.id} promoted to BOTD by #{params.acting_user.email}"

      # Clear first
      DevBuild.where(build_of_the_day: true).each{|build|
        build.build_of_the_day = false
        build.save!
      }

      @build.related.each{|other|
        Rails.logger.info "Also promoting related: #{other.id}"
        other.description = @build.description
        other.keep = true
        other.build_of_the_day = true
        other.save!
      }

      # BOTDs are kept
      @build.keep = true

      @build.build_of_the_day = true
      @build.save!
    }
  end
end
