# frozen_string_literal: true

module BuildOps
  # Marks a build as unverified
  class UnVerifyBuild < Hyperstack::ServerOp
    param :acting_user
    param :build_id
    param :verify_sibling
    validate { params.acting_user.developer? }
    add_error(:build_id, :does_not_exist, 'build does not exist') {
      !(@build = DevBuild.find_by_id(params.build_id))
    }
    step {
      if @build.build_of_the_day
        raise "Can't unverify a BODT"
      end

      Rails.logger.info "Build #{@build.id} unverified by #{params.acting_user.email}"

      if params.verify_sibling
        @build.related.each { |other|
          Rails.logger.info "Also unverifying related: #{other.id}"

          if other.build_of_the_day
            Rails.logger.error "Can't unverify a related build that is BODT"
            next
          end

          other.verified = false
          other.save!
        }
      end

      @build.verified = false
      @build.save!
    }
  end
end
